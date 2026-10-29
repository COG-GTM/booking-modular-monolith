using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Test;

public class GatewayDockerConfigurationTests
{
    [Fact]
    public void docker_clusters_and_discovery_use_the_compose_monolith_service()
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
        var service = Regex.Match(compose, @"(?m)^  (booking_modular_monolith):\s*$");
        Assert.True(service.Success);
        var address = $"http://{service.Groups[1].Value}:80/";
        var clusters = config.GetSection("ReverseProxy:Clusters").GetChildren().ToList();
        Assert.Equal(["booking", "flight", "identity", "monolith", "passenger"], clusters.Select(c => c.Key).Order());
        Assert.All(clusters, cluster => Assert.Equal(address, cluster["Destinations:primary:Address"]));
        Assert.Equal(address + ".well-known/openid-configuration", config["Jwt:MetadataAddress"]);
        Assert.Contains("- ASPNETCORE_ENVIRONMENT=docker", compose, StringComparison.Ordinal);
        Assert.Contains("- booking_modular_monolith", compose, StringComparison.Ordinal);
    }
}
