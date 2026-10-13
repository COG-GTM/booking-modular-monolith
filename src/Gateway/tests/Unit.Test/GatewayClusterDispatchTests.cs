using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Gateway.Unit.Test;

/// <summary>
/// Gives every cluster a distinct destination (a path prefix on a single stub) so the tests can observe
/// which cluster YARP actually dispatched a request to, instead of only inspecting the loaded config.
/// </summary>
public class GatewayClusterDispatchTests : IClassFixture<ClusterEchoStub>, IAsyncLifetime
{
    private const string Audience = "booking-modular-monolith";
    private const string TrustedProxy = "10.0.0.2";
    private const string PublicClient = "203.0.113.10";
    private static readonly string[] Clusters = ["flight", "passenger", "booking", "identity", "monolith"];

    private readonly ClusterEchoStub _downstream;
    private WebApplicationFactory<Program> _factory = null!;

    public GatewayClusterDispatchTests(ClusterEchoStub downstream)
    {
        _downstream = downstream;
    }

    public Task InitializeAsync()
    {
        _factory = CreateFactory(peerAddress: null);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights", "flight")]
    [InlineData("/api/v2/flight", "flight")]
    [InlineData("/api/v1/passenger/1", "passenger")]
    [InlineData("/api/v1/booking", "booking")]
    [InlineData("/api/v1/identity/register-user", "identity")]
    [InlineData("/api/v1/flights/anything", "monolith")]
    [InlineData("/api/v1/bookings", "monolith")]
    [InlineData("/api/v1", "monolith")]
    [InlineData("/api", "monolith")]
    public async Task authenticated_api_request_is_dispatched_to_the_cluster_owning_the_module(
        string path,
        string expectedCluster
    )
    {
        var response = await AuthenticatedClient().GetAsync(path);
        var echo = await ReadEchoAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCluster, echo.Cluster);
        Assert.Equal(path, echo.Path);
    }

    [Theory]
    [InlineData("/connect/token", "identity")]
    [InlineData("/connect/authorize", "identity")]
    [InlineData("/.well-known/openid-configuration", "identity")]
    [InlineData("/.well-known/jwks", "identity")]
    [InlineData("/swagger/index.html", "monolith")]
    [InlineData("/scalar/v1", "monolith")]
    [InlineData("/Account/Login", "monolith")]
    [InlineData("/", "monolith")]
    public async Task anonymous_request_is_dispatched_to_the_identity_or_monolith_cluster(
        string path,
        string expectedCluster
    )
    {
        var response = await _factory.CreateClient().GetAsync(path);
        var echo = await ReadEchoAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCluster, echo.Cluster);
        Assert.Equal(path, echo.Path);
    }

    [Fact]
    public async Task query_string_method_and_body_reach_the_downstream_unchanged()
    {
        const string path = "/api/v1/flight/get-available-flights";
        const string query = "?page=2&pageSize=25&sort=departure%20desc";

        var response = await AuthenticatedClient()
            .PostAsync(path + query, JsonContent.Create(new { FlightNumber = "BA123" }));
        var echo = await ReadEchoAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("flight", echo.Cluster);
        Assert.Equal(HttpMethods.Post, echo.Method);
        Assert.Equal(path, echo.Path);
        Assert.Equal(query, echo.QueryString);
        Assert.Equal("""{"flightNumber":"BA123"}""", echo.Body);
    }

    [Fact]
    public async Task downstream_sees_the_public_client_when_the_gateway_sits_behind_a_trusted_proxy()
    {
        using var factory = CreateFactory(
            IPAddress.Parse(TrustedProxy),
            ("TrustedProxyOptions:KnownProxies:0", TrustedProxy)
        );

        var response = await factory.CreateClient().SendAsync(ForwardedRequest("/.well-known/jwks"));
        var echo = await ReadEchoAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PublicClient, echo.ForwardedFor);
        Assert.Equal("https", echo.ForwardedProto);
    }

    [Fact]
    public async Task downstream_sees_the_peer_when_forwarded_headers_come_from_an_untrusted_source()
    {
        using var factory = CreateFactory(IPAddress.Parse(TrustedProxy));

        var response = await factory.CreateClient().SendAsync(ForwardedRequest("/.well-known/jwks"));
        var echo = await ReadEchoAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TrustedProxy, echo.ForwardedFor);
        Assert.Equal("http", echo.ForwardedProto);
    }

    [Fact]
    public async Task rate_limited_requests_never_reach_the_downstream()
    {
        using var factory = CreateFactory(
            peerAddress: null,
            ("RateLimitOptions:PermitLimit", "2"),
            ("RateLimitOptions:WindowSeconds", "60")
        );
        var client = factory.CreateClient();
        var before = _downstream.RequestCount;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.GetAsync("/swagger/index.html")).StatusCode);

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses
        );
        Assert.Equal(2, _downstream.RequestCount - before);
    }

    [Fact]
    public async Task unauthenticated_api_requests_never_reach_the_downstream()
    {
        var before = _downstream.RequestCount;

        var response = await _factory.CreateClient().GetAsync("/api/v1/booking/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, _downstream.RequestCount);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.SetFakeBearerToken(
            new Dictionary<string, object> { { ClaimTypes.Name, "test" }, { "scope", Audience } }
        );
        return client;
    }

    private static HttpRequestMessage ForwardedRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", PublicClient);
        request.Headers.Add("X-Forwarded-Proto", "https");
        return request;
    }

    private static async Task<ClusterEchoStub.Echo> ReadEchoAsync(HttpResponseMessage response)
    {
        var echo = await response.Content.ReadFromJsonAsync<ClusterEchoStub.Echo>();
        Assert.NotNull(echo);
        return echo;
    }

    private WebApplicationFactory<Program> CreateFactory(
        IPAddress? peerAddress,
        params (string Key, string Value)[] settings
    )
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");

            foreach (var cluster in Clusters)
                builder.UseSetting(
                    $"ReverseProxy:Clusters:{cluster}:Destinations:monolith:Address",
                    _downstream.AddressFor(cluster)
                );

            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
            {
                if (peerAddress is not null)
                    services.AddSingleton<IStartupFilter>(new PeerAddressStartupFilter(peerAddress));

                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = FakeJwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = FakeJwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddFakeJwtBearer();

                services.AddAuthorization(options =>
                    options.AddPolicy(
                        "ApiScope",
                        policy =>
                        {
                            policy.AddAuthenticationSchemes(FakeJwtBearerDefaults.AuthenticationScheme);
                            policy.RequireAuthenticatedUser();
                            policy.RequireClaim("scope", Audience);
                        }
                    )
                );
            });
        });
    }

    private sealed class PeerAddressStartupFilter(IPAddress peerAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    (context, nextMiddleware) =>
                    {
                        context.Connection.RemoteIpAddress = peerAddress;
                        return nextMiddleware(context);
                    }
                );
                next(app);
            };
    }
}

/// <summary>
/// One downstream server that answers under <c>/clusters/{cluster}/...</c>; YARP appends the request path
/// to the destination address, so the prefix identifies the cluster that handled the request.
/// </summary>
public sealed class ClusterEchoStub : IAsyncLifetime
{
    private WebApplication _app = null!;
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public string AddressFor(string cluster) => $"{_app.Urls.Single()}/clusters/{cluster}";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.Map(
            "/clusters/{cluster}/{**path}",
            async (HttpContext context, string cluster, string? path) =>
            {
                Interlocked.Increment(ref _requestCount);

                using var reader = new StreamReader(context.Request.Body);

                return Results.Json(
                    new Echo(
                        cluster,
                        context.Request.Method,
                        "/" + (path ?? string.Empty),
                        context.Request.QueryString.Value ?? string.Empty,
                        await reader.ReadToEndAsync(),
                        context.Request.Headers["X-Forwarded-For"].ToString(),
                        context.Request.Headers["X-Forwarded-Proto"].ToString()
                    )
                );
            }
        );

        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    public sealed record Echo(
        string Cluster,
        string Method,
        string Path,
        string QueryString,
        string Body,
        string ForwardedFor,
        string ForwardedProto
    );
}
