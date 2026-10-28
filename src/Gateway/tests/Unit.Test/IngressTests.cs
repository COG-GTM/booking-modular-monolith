using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Tests;

public class IngressTests
{
    [Fact]
    public void MetadataOverrideIsUsedByConfigurationManager()
    {
        const string metadataAddress = "http://identity-internal/.well-known/openid-configuration";
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Jwt:MetadataAddress"] = metadataAddress }
                    )
            )
        );
        var options = app
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(metadataAddress, options.MetadataAddress);
        var manager =
            Assert.IsType<Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>>(
                options.ConfigurationManager
            );
        Assert.Equal(metadataAddress, manager.MetadataAddress);
        Assert.False(options.RequireHttpsMetadata);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    public void ModuleRoutesUseIndependentClusters(string module)
    {
        using var services = CreateServices();
        var config = services.GetRequiredService<IProxyConfigProvider>().GetConfig();
        var route = Assert.Single(config.Routes, route => route.RouteId == module);
        Assert.Equal(module, route.ClusterId);
        Assert.Equal($"/api/v{{version}}/{module}/{{**remainder}}", route.Match.Path);
        Assert.Equal("authenticated", route.AuthorizationPolicy);
        Assert.Equal(
            "http://localhost:3001/",
            Assert.Single(config.Clusters, c => c.ClusterId == module).Destinations!["primary"].Address
        );
    }

    [Fact]
    public void ConfigurationOverrideRepointsOnlyOneCluster()
    {
        using var services = CreateServices(
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:flight:Destinations:primary:Address"] = "http://flight-service/",
            }
        );
        var clusters = services.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters;
        Assert.Equal(
            "http://flight-service/",
            Assert.Single(clusters, c => c.ClusterId == "flight").Destinations!["primary"].Address
        );
        Assert.All(
            clusters.Where(c => c.ClusterId != "flight"),
            cluster => Assert.Equal("http://localhost:3001/", cluster.Destinations!["primary"].Address)
        );
    }

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights")]
    [InlineData("/api/v1/passenger/1")]
    [InlineData("/api/v1/booking/1")]
    [InlineData("/api/v1/identity/register-user")]
    public async Task ModuleEndpointsRejectMissingTokens(string path)
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthIsAnonymous(string path)
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTokenIsRejected()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-token");
        using var response = await client.GetAsync("/api/v1/flight/get-available-flights");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousProxyRequestsAreRateLimitedButHealthIsNot()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["RateLimiting:PermitLimit"] = "1",
                            ["ReverseProxy:Clusters:monolith:Destinations:primary:Address"] = "http://127.0.0.1:1/",
                        }
                    )
            )
        );
        using var client = app.CreateClient();
        using var first = await client.GetAsync("/unmatched");
        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        using var second = await client.GetAsync("/unmatched");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Theory]
    [InlineData("https://allowed.example", true)]
    [InlineData("https://other.example", false)]
    public async Task CorsAllowsOnlyConfiguredOrigins(string origin, bool allowed)
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "https://allowed.example" }
                    )
            )
        );
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/flight");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static ServiceProvider CreateServices(Dictionary<string, string?>? overrides = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway.json"))
            .AddInMemoryCollection(overrides ?? [])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));
        return services.BuildServiceProvider();
    }
}
