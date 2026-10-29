using System.Text.RegularExpressions;
using Xunit;

namespace Gateway.Test;

public class ServicesComposeConfigurationTests
{
    private static readonly string[] AppServices =
    [
        "identity-service",
        "flight-service",
        "passenger-service",
        "booking-service",
        "gateway",
    ];

    [Fact]
    public void services_compose_declares_independent_images_and_includes_shared_infrastructure()
    {
        var root = FindRepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root.FullName, "deployments/docker-compose/docker-compose.yaml"));

        Assert.Contains("include:", compose, StringComparison.Ordinal);
        Assert.Contains("path: docker-compose.infrastructure.yaml", compose, StringComparison.Ordinal);

        foreach (var service in AppServices)
            Assert.True(Regex.IsMatch(compose, $@"(?m)^  {Regex.Escape(service)}:\s*$"));

        foreach (var service in AppServices)
        {
            var section = ServiceSection(compose, service);
            var dockerfile = Regex.Match(section, @"(?m)^\s+dockerfile:\s*(?<path>\S+)$");
            Assert.True(dockerfile.Success, $"{service} has no Dockerfile build path.");
            Assert.True(
                File.Exists(Path.Combine(root.FullName, dockerfile.Groups["path"].Value)),
                $"{service} Dockerfile does not exist."
            );
        }
    }

    [Fact]
    public void gateway_hosts_and_grpc_targets_use_the_services_topology()
    {
        var root = FindRepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root.FullName, "deployments/docker-compose/docker-compose.yaml"));
        var gateway = ServiceSection(compose, "gateway");

        var clusters = new Dictionary<string, string>
        {
            ["identity"] = "identity-service",
            ["flight"] = "flight-service",
            ["passenger"] = "passenger-service",
            ["booking"] = "booking-service",
        };
        foreach (var (cluster, expectedService) in clusters)
        {
            var address = Regex.Match(
                gateway,
                $@"(?m)^\s+ReverseProxy__Clusters__{Regex.Escape(cluster)}__Destinations__primary__Address:\s*http://(?<service>[\w-]+):80/$"
            );
            Assert.True(address.Success, $"Gateway cluster {cluster} has no Compose destination override.");
            Assert.Equal(expectedService, address.Groups["service"].Value);
            Assert.Contains($"  {address.Groups["service"].Value}:", compose, StringComparison.Ordinal);
        }

        Assert.Contains("Jwt__Authority: http://localhost:5000", gateway, StringComparison.Ordinal);
        Assert.Contains(
            "Jwt__MetadataAddress: http://identity-service:80/.well-known/openid-configuration",
            gateway,
            StringComparison.Ordinal
        );

        var identity = ServiceSection(compose, "identity-service");
        Assert.Contains("Jwt__Authority: http://localhost:5000", identity, StringComparison.Ordinal);
        Assert.Contains(
            "Jwt__MetadataAddress: http://localhost:80/.well-known/openid-configuration",
            identity,
            StringComparison.Ordinal
        );

        foreach (var service in AppServices.Skip(1).Take(3))
        {
            var section = ServiceSection(compose, service);
            Assert.Contains("Jwt__Authority: http://localhost:5000", section, StringComparison.Ordinal);
            Assert.Contains(
                "Jwt__MetadataAddress: http://identity-service:80/.well-known/openid-configuration",
                section,
                StringComparison.Ordinal
            );
        }

        var booking = ServiceSection(compose, "booking-service");
        Assert.Contains(
            "FLIGHT_GRPC_ADDRESS_FOR_SERVICES:-http://flight-service:81",
            booking,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "PASSENGER_GRPC_ADDRESS_FOR_SERVICES:-http://passenger-service:81",
            booking,
            StringComparison.Ordinal
        );
        Assert.Contains("flight-service", compose, StringComparison.Ordinal);
        Assert.Contains("passenger-service", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void base_services_topology_uses_host_defaults_without_cutover_state()
    {
        var root = FindRepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root.FullName, "deployments/docker-compose/docker-compose.yaml"));

        foreach (var service in AppServices)
        {
            var section = ServiceSection(compose, service);
            Assert.DoesNotContain("BackgroundProcessingEnabled", section, StringComparison.Ordinal);
            Assert.DoesNotContain("${", section, StringComparison.Ordinal);
            Assert.DoesNotContain("monolith", section, StringComparison.OrdinalIgnoreCase);
        }

        var booking = ServiceSection(compose, "booking-service");
        Assert.Contains("Services__flight__grpc__0: http://flight-service:81", booking, StringComparison.Ordinal);
        Assert.Contains("Services__passenger__grpc__0: http://passenger-service:81", booking, StringComparison.Ordinal);
    }

    [Fact]
    public void cutover_overlay_sets_state_driven_background_and_monolith_grpc_overrides()
    {
        var root = FindRepositoryRoot();
        var overlay = File.ReadAllText(
            Path.Combine(root.FullName, "deployments/docker-compose/docker-compose.cutover.yaml")
        );
        var expected = new Dictionary<string, string>
        {
            ["identity-service"] = "IDENTITY",
            ["flight-service"] = "FLIGHT",
            ["passenger-service"] = "PASSENGER",
            ["booking-service"] = "BOOKING",
        };

        foreach (var (service, module) in expected)
        {
            var section = ServiceSection(overlay, service);
            Assert.Contains(
                $"Modules__{module}__BackgroundProcessingEnabled: ${{{module}_SERVICE_BACKGROUND:-false}}",
                section,
                StringComparison.Ordinal
            );
        }

        var booking = ServiceSection(overlay, "booking-service");
        Assert.Contains(
            "Services__flight__grpc__0: ${FLIGHT_GRPC_ADDRESS_FOR_SERVICES:-http://monolith:81}",
            booking,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Services__passenger__grpc__0: ${PASSENGER_GRPC_ADDRESS_FOR_SERVICES:-http://monolith:81}",
            booking,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void app_services_use_readiness_probes_and_configured_otel_names()
    {
        var compose = File.ReadAllText(
            Path.Combine(FindRepositoryRoot().FullName, "deployments/docker-compose/docker-compose.yaml")
        );
        var serviceNames = new Dictionary<string, string>
        {
            ["identity-service"] = "identity-service",
            ["flight-service"] = "flight-service",
            ["passenger-service"] = "passenger-service",
            ["booking-service"] = "booking-service",
            ["gateway"] = "booking-gateway",
        };

        foreach (var (service, serviceName) in serviceNames)
        {
            var section = ServiceSection(compose, service);
            Assert.Contains($"OTEL_SERVICE_NAME: {serviceName}", section, StringComparison.Ordinal);
            Assert.Contains(
                "OTEL_EXPORTER_OTLP_ENDPOINT: http://otel-collector:4317",
                section,
                StringComparison.Ordinal
            );
            Assert.Contains("GET /health/ready HTTP/1.1", section, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void non_gateway_application_ports_are_loopback_bound()
    {
        var compose = File.ReadAllText(
            Path.Combine(FindRepositoryRoot().FullName, "deployments/docker-compose/docker-compose.yaml")
        );

        foreach (var service in AppServices)
        {
            var section = ServiceSection(compose, service);
            var ports = Regex.Matches(section, "(?m)^\\s+- \"(?<mapping>[^\"]+)\"\\s*$");
            foreach (Match port in ports)
            {
                var mapping = Regex.Match(
                    port.Groups["mapping"].Value,
                    @"^(?:(?<ip>[\d.]+):)?(?<published>\d+):(?<target>\d+)$"
                );
                Assert.True(mapping.Success, $"Unexpected app port mapping: {port.Groups["mapping"].Value}");
                if (mapping.Groups["published"].Value == "5000")
                {
                    Assert.Equal("gateway", service);
                    continue;
                }

                Assert.Equal("127.0.0.1", mapping.Groups["ip"].Value);
            }
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "booking-modular-monolith.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return root;
    }

    private static string ServiceSection(string compose, string service)
    {
        var section = Regex.Match(
            compose,
            $@"(?ms)^  {Regex.Escape(service)}:\s*(?<section>.*?)(?=^  [a-zA-Z0-9_-]+:\s*$|\z)"
        );
        Assert.True(section.Success, $"Compose service {service} is missing.");
        return section.Groups["section"].Value;
    }
}
