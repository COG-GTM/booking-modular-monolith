using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Test;

public class GatewayDockerConfigurationTests
{
    [Fact]
    public void docker_clusters_and_discovery_use_the_services_topology()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "booking-modular-monolith.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var config = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root.FullName, "src/Gateway/src"))
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.docker.json")
            .Build();
        var compose = File.ReadAllText(Path.Combine(root.FullName, "deployments/docker-compose/docker-compose.yaml"));
        var clusters = config.GetSection("ReverseProxy:Clusters").GetChildren().ToList();
        Assert.Equal(["booking", "flight", "identity", "passenger"], clusters.Select(c => c.Key).Order());
        var destinations = new Dictionary<string, string>
        {
            ["identity"] = "identity-service",
            ["flight"] = "flight-service",
            ["passenger"] = "passenger-service",
            ["booking"] = "booking-service",
        };
        foreach (var cluster in clusters)
            Assert.Equal($"http://{destinations[cluster.Key]}:80/", cluster["Destinations:primary:Address"]);
        Assert.Equal("http://identity-service:80/.well-known/openid-configuration", config["Jwt:MetadataAddress"]);
        Assert.DoesNotContain("ReverseProxy__Clusters__monolith", compose, StringComparison.Ordinal);
        var routes = string.Join(
            ",",
            config.GetSection("ReverseProxy:Routes").GetChildren().Select(route => route.Key)
        );
        Assert.DoesNotContain("api-fallback", routes, StringComparison.Ordinal);
    }
}
