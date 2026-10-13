using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Unit.Test;

/// <summary>
/// docker-compose runs the gateway with <c>ASPNETCORE_ENVIRONMENT=docker</c>; these tests load that
/// layer the same way the container does and check every context cluster points at its service container
/// (the monolith host is decommissioned, AB-505).
/// </summary>
public class GatewayDockerConfigurationTests : IAsyncLifetime
{
    private const string IdentityContainer = "http://identity-service:80";
    private const string PublicAuthority = "https://localhost:3000";
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("docker");
            builder.UseSetting("ObservabilityOptions:UsePrometheusExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseOTLPExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseAspireOTLPExporter", "false");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("identity", "http://identity-service:80")]
    [InlineData("flight", "http://flight-service:80")]
    [InlineData("passenger", "http://passenger-service:80")]
    [InlineData("booking", "http://booking-service:80")]
    // Catch-all: no monolith is left, so unmatched paths go to Identity (UI pages, discovery).
    [InlineData("monolith", IdentityContainer)]
    public void every_cluster_targets_its_service_container(string clusterId, string address)
    {
        var config = _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

        var cluster = Assert.Single(config.Clusters, c => c.ClusterId == clusterId);
        var destination = Assert.Single(cluster.Destinations!);
        Assert.Equal("monolith", destination.Key);
        Assert.Equal(address, destination.Value.Address);
    }

    [Fact]
    public void no_cluster_targets_the_decommissioned_monolith_container()
    {
        var config = _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

        Assert.Equal(
            ["booking", "flight", "identity", "monolith", "passenger"],
            config.Clusters.Select(c => c.ClusterId).Order()
        );
        Assert.DoesNotContain(
            config.Clusters.SelectMany(c => c.Destinations!.Values),
            d => d.Address.Contains("booking_modular_monolith", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void routes_are_inherited_from_the_base_configuration()
    {
        var config = _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

        Assert.Equal(
            [
                "api-fallback",
                "booking",
                "flight",
                "identity",
                "identity-connect",
                "identity-discovery",
                "monolith-fallback",
                "passenger",
            ],
            config.Routes.Select(r => r.RouteId).Order()
        );
    }

    [Fact]
    public void discovery_is_fetched_over_the_container_network_but_tokens_are_validated_against_the_public_issuer()
    {
        var options = _factory
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal($"{IdentityContainer}/.well-known/openid-configuration", options.MetadataAddress);
        Assert.Equal(PublicAuthority, options.Authority);
        Assert.Equal([PublicAuthority], options.TokenValidationParameters.ValidIssuers);
        Assert.Equal(["booking-modular-monolith"], options.TokenValidationParameters.ValidAudiences);
    }
}
