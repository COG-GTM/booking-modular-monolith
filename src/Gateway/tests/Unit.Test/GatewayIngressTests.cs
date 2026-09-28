using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Duende.IdentityServer.EntityFramework.Entities;
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

public class GatewayIngressTests : IClassFixture<DownstreamStub>, IAsyncLifetime
{
    private const string Audience = "booking-modular-monolith";
    private readonly DownstreamStub _downstream;
    private WebApplicationFactory<Program> _factory = null!;

    public GatewayIngressTests(DownstreamStub downstream)
    {
        _downstream = downstream;
    }

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");
            builder.UseSetting("RateLimitOptions:PermitLimit", "3");
            builder.UseSetting("RateLimitOptions:WindowSeconds", "60");

            foreach (var cluster in new[] { "flight", "passenger", "booking", "identity", "monolith" })
                builder.UseSetting(
                    $"ReverseProxy:Clusters:{cluster}:Destinations:monolith:Address",
                    _downstream.Address
                );

            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = FakeJwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = FakeJwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddFakeJwtBearer();

                services.AddAuthorization(options =>
                    options.AddPolicy(
                        nameof(ApiScope),
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

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task module_route_without_token_is_rejected_at_the_gateway()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task module_route_with_wrong_scope_is_forbidden_at_the_gateway()
    {
        var client = _factory.CreateClient();
        client.SetFakeBearerToken(
            new Dictionary<string, object> { { ClaimTypes.Name, "test" }, { "scope", "other-api" } }
        );

        var response = await client.GetAsync("/api/v1/booking/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights")]
    [InlineData("/api/v1/passenger/1")]
    [InlineData("/api/v1/booking")]
    [InlineData("/api/v1/identity/register-user")]
    public async Task authenticated_module_request_is_forwarded_with_auth_and_trace_context(string path)
    {
        var client = _factory.CreateClient();
        client.SetFakeBearerToken(
            new Dictionary<string, object> { { ClaimTypes.Name, "test" }, { "scope", Audience } }
        );
        client.DefaultRequestHeaders.Add("correlationId", "abc-123");

        var response = await client.GetAsync(path);
        var echo = await response.Content.ReadFromJsonAsync<DownstreamStub.Echo>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(echo);
        Assert.Equal(path, echo.Path);
        Assert.StartsWith("FakeBearer ", echo.Authorization, StringComparison.Ordinal);
        Assert.Equal("abc-123", echo.CorrelationId);
        Assert.False(string.IsNullOrEmpty(echo.TraceParent));
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/swagger/index.html")]
    [InlineData("/")]
    public async Task anonymous_routes_are_forwarded_without_a_token(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);
        var echo = await response.Content.ReadFromJsonAsync<DownstreamStub.Echo>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(path, echo!.Path);
        Assert.False(string.IsNullOrEmpty(echo.CorrelationId));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task health_probes_are_answered_by_the_gateway_itself(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task cors_preflight_is_answered_by_the_gateway()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/flight/get-available-flights");
        request.Headers.Add("Origin", "https://app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task requests_over_the_limit_are_rejected_with_429()
    {
        var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
            statuses.Add((await client.GetAsync("/.well-known/jwks")).StatusCode);

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            statuses
        );
    }
}

public sealed class DownstreamStub : IAsyncLifetime
{
    private WebApplication _app = null!;

    public string Address { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.Map(
            "/{**path}",
            (HttpContext context) =>
                Results.Json(
                    new Echo(
                        context.Request.Path,
                        context.Request.Headers.Authorization.ToString(),
                        context.Request.Headers["correlationId"].ToString(),
                        context.Request.Headers["traceparent"].ToString()
                    )
                )
        );

        await _app.StartAsync();
        Address = _app.Urls.Single();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    public sealed record Echo(string Path, string Authorization, string CorrelationId, string TraceParent);
}
