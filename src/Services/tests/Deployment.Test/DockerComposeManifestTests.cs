using FluentAssertions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Deployment.Test;

public class DockerComposeManifestTests
{
    private static readonly YamlMappingNode Services = LoadServices();

    public static TheoryData<string> ApplicationServiceNames =>
        new("identity-service", "flight-service", "passenger-service", "booking-service", "gateway");

    [Fact]
    public void should_replace_monolith_container_with_per_service_containers()
    {
        var serviceNames = Services.Children.Keys.Select(k => k.ToString()).ToList();

        serviceNames.Should().NotContain("booking_modular_monolith");
        serviceNames
            .Should()
            .Contain(["identity-service", "flight-service", "passenger-service", "booking-service", "gateway"]);
    }

    [Theory]
    [InlineData("identity-service", "Identity", "IdentityService")]
    [InlineData("flight-service", "Flight", "FlightService")]
    [InlineData("passenger-service", "Passenger", "PassengerService")]
    [InlineData("booking-service", "Booking", "BookingService")]
    [InlineData("gateway", "Gateway", "GatewayService")]
    public void should_build_each_service_from_shared_dockerfile_with_existing_project(
        string serviceName,
        string serviceDir,
        string projectName)
    {
        var build = GetMapping(GetService(serviceName), "build");
        var args = GetMapping(build, "args");

        GetScalar(build, "dockerfile").Should().Be("src/Services/Dockerfile");
        GetScalar(build, "context").Should().Be("../../");
        GetScalar(args, "SERVICE_DIR").Should().Be(serviceDir);
        GetScalar(args, "SERVICE_NAME").Should().Be(projectName);

        var projectFile = Path.Combine(RepositoryPaths.Root, "src", "Services", serviceDir, $"{projectName}.csproj");
        File.Exists(projectFile).Should().BeTrue($"the compose build args must point at an existing project ({projectFile})");
    }

    [Theory]
    [InlineData("flight-service", "7001:80")]
    [InlineData("passenger-service", "7002:80")]
    [InlineData("identity-service", "7003:80")]
    [InlineData("booking-service", "7004:80")]
    [InlineData("gateway", "3001:80")]
    public void should_expose_each_service_on_its_dedicated_host_port(string serviceName, string mapping)
    {
        GetSequence(GetService(serviceName), "ports").Should().ContainSingle().Which.Should().Be(mapping);
    }

    [Theory]
    [MemberData(nameof(ApplicationServiceNames))]
    public void should_run_each_service_in_docker_environment_on_booking_network(string serviceName)
    {
        var service = GetService(serviceName);

        GetEnvironment(service).Should().ContainKey("ASPNETCORE_ENVIRONMENT").WhoseValue.Should().Be("docker");
        GetSequence(service, "networks").Should().ContainSingle().Which.Should().Be("booking");
    }

    [Theory]
    [InlineData("flight-service")]
    [InlineData("passenger-service")]
    [InlineData("booking-service")]
    public void should_point_downstream_services_at_identity_authority(string serviceName)
    {
        var environment = GetEnvironment(GetService(serviceName));

        environment.Should().ContainKey("Jwt__Authority").WhoseValue.Should().Be("http://identity-service:80");
        GetSequence(GetService(serviceName), "depends_on").Should().Contain("identity-service");
    }

    [Theory]
    [InlineData("identity-service", "persist_message_identity")]
    [InlineData("flight-service", "persist_message_flight")]
    [InlineData("passenger-service", "persist_message_passenger")]
    [InlineData("booking-service", "persist_message_booking")]
    public void should_give_each_service_its_own_outbox_database(string serviceName, string databaseName)
    {
        var environment = GetEnvironment(GetService(serviceName));

        environment.Should().ContainKey("PersistMessageOptions__ConnectionString");
        environment["PersistMessageOptions__ConnectionString"].Should().Contain($"Database={databaseName};");
    }

    [Fact]
    public void should_use_event_store_only_for_booking_service()
    {
        GetEnvironment(GetService("booking-service"))
            .Should()
            .ContainKey("EventStoreOptions__ConnectionString")
            .WhoseValue.Should()
            .Be("esdb://eventstore:2113?tls=false");

        GetSequence(GetService("booking-service"), "depends_on").Should().Contain("eventstore");
    }

    [Theory]
    [InlineData("flight", "flight-service")]
    [InlineData("passenger", "passenger-service")]
    [InlineData("identity", "identity-service")]
    [InlineData("booking", "booking-service")]
    public void should_override_gateway_cluster_destinations_with_compose_service_names(
        string clusterName,
        string serviceName)
    {
        var environment = GetEnvironment(GetService("gateway"));
        var key = $"ReverseProxy__Clusters__{clusterName}-cluster__Destinations__{clusterName}-destination__Address";

        environment.Should().ContainKey(key).WhoseValue.Should().Be($"http://{serviceName}:80");
    }

    [Fact]
    public void should_start_gateway_after_all_four_service_hosts()
    {
        GetSequence(GetService("gateway"), "depends_on")
            .Should()
            .BeEquivalentTo("identity-service", "flight-service", "passenger-service", "booking-service");
    }

    private static YamlMappingNode LoadServices()
    {
        using var reader = new StreamReader(RepositoryPaths.DockerComposeFile);
        var stream = new YamlStream();
        stream.Load(reader);

        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return (YamlMappingNode)root.Children[new YamlScalarNode("services")];
    }

    private static YamlMappingNode GetService(string name)
    {
        return (YamlMappingNode)Services.Children[new YamlScalarNode(name)];
    }

    private static YamlMappingNode GetMapping(YamlMappingNode node, string key)
    {
        return (YamlMappingNode)node.Children[new YamlScalarNode(key)];
    }

    private static string GetScalar(YamlMappingNode node, string key)
    {
        return ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value!;
    }

    private static List<string> GetSequence(YamlMappingNode node, string key)
    {
        return ((YamlSequenceNode)node.Children[new YamlScalarNode(key)])
            .Children.Select(c => c.ToString())
            .ToList();
    }

    private static Dictionary<string, string> GetEnvironment(YamlMappingNode service)
    {
        return GetSequence(service, "environment")
            .Select(entry => entry.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);
    }
}
