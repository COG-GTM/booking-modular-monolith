using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Tests;

public class GatewayDispatchTests
{
    private const string Issuer = "https://localhost:3000";
    private const string Audience = "booking-modular-monolith";
    private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights", "flight")]
    [InlineData("/api/v1/passenger/1", "passenger")]
    [InlineData("/api/v1/booking/1", "booking")]
    [InlineData("/api/v1/identity/register-user", "identity")]
    public async Task AuthenticatedRequestsReachTheirClusterAndForwardBearerHeader(string path, string cluster)
    {
        await using var fixture = await ProxyFixture.CreateAsync();

        var response = await fixture.SendAuthenticatedAsync(path, "test-user");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/clusters/{cluster}{path}|True", response.Body);
    }

    [Fact]
    public async Task RateLimitPartitionsAuthenticatedRequestsBySubject()
    {
        await using var fixture = await ProxyFixture.CreateAsync(permitLimit: 1);
        const string path = "/api/v1/flight/get-available-flights";

        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAuthenticatedAsync(path, "user-a")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await fixture.SendAuthenticatedAsync(path, "user-a")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendAuthenticatedAsync(path, "user-b")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedSubjectHasSeparatePartitionFromExhaustedAnonymousPartition()
    {
        await using var fixture = await ProxyFixture.CreateAsync(permitLimit: 1);

        Assert.Equal(HttpStatusCode.OK, await fixture.SendAnonymousAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, await fixture.SendAnonymousAsync());
        Assert.Equal(
            HttpStatusCode.OK,
            (await fixture.SendAuthenticatedAsync("/api/v1/flight/get-available-flights", "user-a")).StatusCode
        );
    }

    [Fact]
    public async Task AnonymousRateLimitReplenishesAfterWindow()
    {
        await using var fixture = await ProxyFixture.CreateAsync(permitLimit: 1, windowSeconds: 1);

        Assert.Equal(HttpStatusCode.OK, await fixture.SendAnonymousAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, await fixture.SendAnonymousAsync());

        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        var replenished = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            if (await fixture.SendAnonymousAsync() == HttpStatusCode.OK)
            {
                replenished = true;
                break;
            }
        }

        Assert.True(replenished, "The anonymous rate-limit partition did not replenish within three seconds.");
    }

    [Fact]
    public void AnonymousRoutesUseIdentityAndNoMonolithFallbackExists()
    {
        using var services = CreateServices();
        var routes = services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes;

        Assert.DoesNotContain(routes, route => route.RouteId == "fallback");
        Assert.DoesNotContain(
            services.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters,
            cluster => cluster.ClusterId == "monolith"
        );

        foreach (var routeId in new[] { "connect", "discovery" })
        {
            var route = Assert.Single(routes, candidate => candidate.RouteId == routeId);
            Assert.Equal("identity", route.ClusterId);
            Assert.Equal("anonymous", route.AuthorizationPolicy);
        }
    }

    private static ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway.json"))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));
        return services.BuildServiceProvider();
    }

    private static string CreateToken(string subject) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity([new Claim("sub", subject)]),
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            }
        );

    private sealed class ProxyFixture : IAsyncDisposable
    {
        private readonly WebApplication _upstream;
        private readonly WebApplicationFactory<Program> _gateway;

        private ProxyFixture(WebApplication upstream, WebApplicationFactory<Program> gateway)
        {
            _upstream = upstream;
            _gateway = gateway;
            Client = gateway.CreateClient();
        }

        public HttpClient Client { get; }

        public static async Task<ProxyFixture> CreateAsync(int permitLimit = 100, int windowSeconds = 60)
        {
            var backendBuilder = WebApplication.CreateBuilder();
            backendBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var upstream = backendBuilder.Build();
            upstream.MapFallback(context =>
                context.Response.WriteAsync(
                    $"{context.Request.Path}|{context.Request.Headers.ContainsKey("Authorization")}"
                )
            );
            await upstream.StartAsync();
            var address = upstream
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();

            var settings = new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:WindowSeconds"] = windowSeconds.ToString(),
            };
            foreach (var cluster in new[] { "identity", "flight", "passenger", "booking" })
                settings[$"ReverseProxy:Clusters:{cluster}:Destinations:primary:Address"] =
                    $"{address}/clusters/{cluster}/";

            var gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
                host.ConfigureTestServices(services =>
                    services.Configure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options =>
                        {
                            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                            options.Configuration.SigningKeys.Add(SigningKey);
                            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                        }
                    )
                );
            });
            return new ProxyFixture(upstream, gateway);
        }

        public async Task<HttpStatusCode> SendAnonymousAsync()
        {
            using var response = await Client.GetAsync("/connect/token");
            return response.StatusCode;
        }

        public async Task<(HttpStatusCode StatusCode, string Body)> SendAuthenticatedAsync(string path, string subject)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(subject));
            using var response = await Client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _gateway.DisposeAsync();
            await _upstream.DisposeAsync();
        }
    }
}
