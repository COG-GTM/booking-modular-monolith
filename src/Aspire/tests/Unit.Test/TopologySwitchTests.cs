using Aspire.Hosting.ApplicationModel;
using Xunit;
using static AppHost.Unit.Test.AppHostModel;

namespace AppHost.Unit.Test;

// Only the services topology is left (src/Api was decommissioned in AB-505).
public class TopologySwitchTests
{
    private static readonly string[] ServiceProjects = ["booking", "flight", "identity", "passenger"];

    // Databases the decommissioned monolith used; the services topology must not recreate them.
    private static readonly string[] MonolithDatabases =
    [
        "flight",
        "passenger",
        "identity",
        "flight-persist-message",
        "passenger-persist-message",
        "identity-persist-message",
        "booking-persist-message",
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task topology_defaults_to_services_when_unset_or_blank(string? topology)
    {
        var configuration = topology is null
            ? null
            : new Dictionary<string, string?> { ["AppHost:Topology"] = topology };
        await using var appHost = await BuildAsync(configuration);

        Assert.Equal(ServiceProjects.Append("gateway").Order(), ProjectNames(appHost));
    }

    [Theory]
    [InlineData("monolith")]
    [InlineData("Monolith")]
    public async Task monolith_topology_is_rejected_after_the_decommission(string topology)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => BuildAsync(new() { ["AppHost:Topology"] = topology }));

        Assert.Contains("removed together with src/Api", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("SERVICES")]
    public async Task services_topology_is_matched_case_insensitively(string topology)
    {
        await using var appHost = await BuildAsync(new() { ["AppHost:Topology"] = topology });

        Assert.Equal(ServiceProjects.Append("gateway").Order(), ProjectNames(appHost));
    }

    [Fact]
    public async Task unknown_topology_error_names_the_accepted_values()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => BuildAsync(new() { ["AppHost:Topology"] = "Mesh" }));
        var message = error.ToString();

        Assert.Contains("'Mesh'", message, StringComparison.Ordinal);
        Assert.Contains("'Services'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task services_topology_registers_no_monolith_databases()
    {
        await using var appHost = await BuildAsync();
        var postgresDatabases = GetResources(appHost.App)
            .OfType<PostgresDatabaseResource>()
            .Select(db => db.Name)
            .ToList();

        Assert.Empty(postgresDatabases.Intersect(MonolithDatabases));
        Assert.All(postgresDatabases, name => Assert.Contains("-service-", name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task services_topology_gateway_keeps_its_ports_and_does_not_depend_on_the_api()
    {
        await using var appHost = await BuildAsync();
        var gateway = GetProject(appHost.App, "gateway");
        var endpoints = gateway.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(5000, endpoints["gateway-http"].Port);
        Assert.Equal(5001, endpoints["gateway-https"].Port);
        Assert.DoesNotContain(gateway.Annotations.OfType<WaitAnnotation>(), wait => wait.Resource.Name == "api");
        Assert.DoesNotContain(env.Keys, key => key.StartsWith("services__api__", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env.Values, value => value.Contains("{api.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task services_topology_sets_every_gateway_cluster_exactly_once()
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, "gateway");
        var clusterKeys = env
            .Keys.Where(key => key.StartsWith("ReverseProxy__Clusters__", StringComparison.Ordinal))
            .Order();

        Assert.Equal(Clusters.Select(ClusterAddressKey).Order(), clusterKeys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task blank_cluster_override_keeps_the_service_destination(string blank)
    {
        await using var appHost = await BuildAsync(new() { ["Gateway:Clusters:booking"] = blank });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal("{booking.bindings.http.url}", env[ClusterAddressKey("booking")], ignoreCase: true);
    }

    [Fact]
    public async Task services_topology_cluster_override_replaces_only_the_named_cluster()
    {
        var extracted = "http://identity-service:80";
        await using var appHost = await BuildAsync(new() { ["Gateway:Clusters:identity"] = extracted });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(extracted, env[ClusterAddressKey("identity")]);
        Assert.Equal("{identity.bindings.http.url}", env[ClusterAddressKey("monolith")], ignoreCase: true);
        Assert.Equal("{flight.bindings.http.url}", env[ClusterAddressKey("flight")], ignoreCase: true);
    }

    [Fact]
    public async Task identity_has_no_read_database_and_no_mongo_settings()
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, "identity");

        Assert.DoesNotContain(GetResources(appHost.App), r => r.Name == "identity-service-read");
        Assert.DoesNotContain(env.Keys, key => key.StartsWith("MongoOptions__", StringComparison.Ordinal));
        Assert.False(env.ContainsKey("ConnectionStrings__identity-mongo"));
        Assert.False(env.ContainsKey("ConnectionStrings__mongo"));
    }

    [Fact]
    public async Task only_flight_and_passenger_expose_grpc_endpoints()
    {
        await using var appHost = await BuildAsync();

        foreach (var service in new[] { "identity", "booking" })
        {
            Assert.DoesNotContain(
                GetProject(appHost.App, service).Annotations.OfType<EndpointAnnotation>(),
                e => e.Name.Equals("grpc", StringComparison.OrdinalIgnoreCase)
            );
        }

        var env = await GetEnvironmentAsync(appHost.App, "booking");
        Assert.DoesNotContain(
            env.Keys,
            key => key.StartsWith("services__identity__", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static IEnumerable<string> ProjectNames(AppHostUnderTest appHost) =>
        GetResources(appHost.App).OfType<ProjectResource>().Select(p => p.Name).Order();
}
