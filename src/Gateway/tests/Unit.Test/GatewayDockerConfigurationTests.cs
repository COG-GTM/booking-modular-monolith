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
/// layer the same way the container does and check it points at the monolith's container name.
/// </summary>
public class GatewayDockerConfigurationTests : IAsyncLifetime
{
    private const string MonolithContainer = "http://booking_modular_monolith:80";
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

    [Fact]
    public void every_cluster_targets_the_monolith_container()
    {
        var config = _factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();

        Assert.Equal(
            ["booking", "flight", "identity", "monolith", "passenger"],
            config.Clusters.Select(c => c.ClusterId).Order()
        );
        Assert.All(
            config.Clusters,
            cluster =>
            {
                var destination = Assert.Single(cluster.Destinations!);
                Assert.Equal("monolith", destination.Key);
                Assert.Equal(MonolithContainer, destination.Value.Address);
            }
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

        Assert.Equal($"{MonolithContainer}/.well-known/openid-configuration", options.MetadataAddress);
        Assert.Equal(PublicAuthority, options.Authority);
        Assert.Equal([PublicAuthority], options.TokenValidationParameters.ValidIssuers);
        Assert.Equal(["booking-modular-monolith"], options.TokenValidationParameters.ValidAudiences);
    }
}
