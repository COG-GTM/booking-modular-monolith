using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppHost.Unit.Test;

/// <summary>
/// Covers the AB-244 AppHost wiring that <see cref="GatewayAppHostWiringTests"/> does not: the renamed Postgres
/// database resources, per-service start-up dependencies and health probes, the topology switch itself and the
/// monolith fallback's resource references.
/// </summary>
public class AppHostTopologyTests
{
    private static readonly string[] Services = ["identity", "flight", "passenger", "booking"];
    private static readonly string[] Clusters = ["flight", "passenger", "booking", "identity", "monolith"];

    [Fact]
    public async Task postgres_databases_are_suffixed_with_db_but_keep_their_database_names()
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();
        var databases = appHost
            .Model.Resources.OfType<PostgresDatabaseResource>()
            .ToDictionary(database => database.Name, database => database.DatabaseName);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["flight-db"] = "flight",
                ["passenger-db"] = "passenger",
                ["identity-db"] = "identity",
                ["booking-db"] = "booking",
            },
            databases
        );
        Assert.All(
            databases.Keys,
            name =>
                Assert.Equal(
                    "postgres",
                    appHost.Model.Resources.OfType<PostgresDatabaseResource>().Single(d => d.Name == name).Parent.Name
                )
        );
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    public async Task service_projects_own_the_logical_names_used_for_discovery(string service)
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();

        var owner = Assert.Single(appHost.Model.Resources, resource => resource.Name == service);
        Assert.IsType<ProjectResource>(owner);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    public async Task each_service_reads_its_database_through_the_unsuffixed_connection_string_name(string service)
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();
        var env = await appHost.GetEnvironmentAsync(service);

        Assert.Equal($"{{{service}-db.connectionString}}", env[$"ConnectionStrings__{service}"]);
        Assert.DoesNotContain($"ConnectionStrings__{service}-db", env.Keys);
    }

    [Theory]
    [InlineData(
        "identity",
        new[] { "identity-db", "rabbitmq" },
        new[] { "mongo", "eventstore", "flight", "passenger", "booking", "api" }
    )]
    [InlineData(
        "flight",
        new[] { "flight-db", "mongo", "rabbitmq" },
        new[] { "eventstore", "identity", "passenger", "booking", "api" }
    )]
    [InlineData(
        "passenger",
        new[] { "passenger-db", "mongo", "rabbitmq" },
        new[] { "eventstore", "identity", "flight", "booking", "api" }
    )]
    [InlineData(
        "booking",
        new[] { "booking-db", "mongo", "eventstore", "rabbitmq", "flight", "passenger" },
        new[] { "identity", "api" }
    )]
    public async Task each_service_waits_only_for_the_infrastructure_it_uses(
        string service,
        string[] expected,
        string[] forbidden
    )
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();
        var waits = appHost
            .GetProject(service)
            .Annotations.OfType<WaitAnnotation>()
            .Select(wait => wait.Resource.Name)
            .ToHashSet();

        Assert.Superset(expected.ToHashSet(), waits);
        Assert.Empty(waits.Intersect(forbidden));
    }

    [Theory]
    [InlineData("identity", "http")]
    [InlineData("flight", "http")]
    [InlineData("passenger", "Http")]
    [InlineData("booking", "http")]
    public async Task each_service_is_probed_on_health_over_its_http_endpoint(string service, string endpointName)
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();
        var project = appHost.GetProject(service);

        var endpoint = Assert.Single(project.Annotations.OfType<EndpointAnnotation>(), e => e.Name == endpointName);
        Assert.Equal("http", endpoint.UriScheme);

        var healthCheck = Assert.Single(project.Annotations.OfType<HealthCheckAnnotation>());
        Assert.StartsWith($"{service}_{endpointName}_/health", healthCheck.Key, StringComparison.Ordinal);
        Assert.DoesNotContain("/alive", healthCheck.Key, StringComparison.Ordinal);
        Assert.Contains(appHost.HealthCheckRegistrations, registration => registration.Name == healthCheck.Key);
    }

    [Fact]
    public async Task standalone_services_never_get_the_shared_mongo_server_connection_string()
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();

        foreach (var service in Services)
        {
            var env = await appHost.GetEnvironmentAsync(service);
            Assert.DoesNotContain("ConnectionStrings__mongo", env.Keys);
        }

        Assert.Empty(
            (await appHost.GetEnvironmentAsync("identity")).Keys.Where(key =>
                key.EndsWith("-read", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task gateway_discovers_every_standalone_service_by_name()
    {
        await using var appHost = await AppHostUnderTest.CreateAsync();
        var env = await appHost.GetEnvironmentAsync("gateway");

        Assert.Equal("{identity.bindings.http.url}", env["services__identity__http__0"]);
        Assert.Equal("{flight.bindings.http.url}", env["services__flight__http__0"]);
        Assert.Equal("{passenger.bindings.Http.url}", env["services__passenger__Http__0"]);
        Assert.Equal("{booking.bindings.http.url}", env["services__booking__http__0"]);
    }

    [Theory]
    [InlineData("monolith")]
    [InlineData("MONOLITH")]
    [InlineData("Monolith")]
    public async Task topology_switch_is_case_insensitive(string topology)
    {
        await using var appHost = await AppHostUnderTest.CreateAsync(new() { ["AppHost:Topology"] = topology });

        Assert.Equal(["api", "gateway"], appHost.ProjectNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Microservices")]
    [InlineData("Hybrid")]
    public async Task any_other_topology_value_runs_the_microservices_layout(string topology)
    {
        await using var appHost = await AppHostUnderTest.CreateAsync(new() { ["AppHost:Topology"] = topology });

        Assert.Equal(["booking", "flight", "gateway", "identity", "passenger"], appHost.ProjectNames);
    }

    [Fact]
    public async Task monolith_api_gets_every_store_under_the_names_the_modules_expect()
    {
        await using var appHost = await AppHostUnderTest.CreateAsync(AppHostUnderTest.Monolith);
        var env = await appHost.GetEnvironmentAsync("api");

        Assert.Equal("{flight-db.connectionString}", env["ConnectionStrings__flight"]);
        Assert.Equal("{passenger-db.connectionString}", env["ConnectionStrings__passenger"]);
        Assert.Equal("{identity-db.connectionString}", env["ConnectionStrings__identity"]);
        Assert.Equal("{booking-db.connectionString}", env["ConnectionStrings__booking"]);

        foreach (
            var name in new[] { "mongo", "flight-read", "passenger-read", "booking-read", "eventstore", "rabbitmq" }
        )
        {
            Assert.Equal($"{{{name}.connectionString}}", env[$"ConnectionStrings__{name}"]);
        }
    }

    [Fact]
    public async Task monolith_api_resolves_flight_and_passenger_discovery_names_to_itself()
    {
        await using var appHost = await AppHostUnderTest.CreateAsync(AppHostUnderTest.Monolith);
        var env = await appHost.GetEnvironmentAsync("api");

        Assert.Equal("{api.bindings.api-https.url}", env["services__flight__https__0"]);
        Assert.Equal("{api.bindings.api-https.url}", env["services__passenger__https__0"]);
        Assert.DoesNotContain("Grpc__Flight__Address", env.Keys);
        Assert.DoesNotContain("Grpc__Passenger__Address", env.Keys);
    }

    [Fact]
    public async Task monolith_api_keeps_its_ports_health_probe_and_dependencies()
    {
        await using var appHost = await AppHostUnderTest.CreateAsync(AppHostUnderTest.Monolith);
        var api = appHost.GetProject("api");

        var endpoints = api.Annotations.OfType<EndpointAnnotation>().ToDictionary(endpoint => endpoint.Name);
        Assert.Equal(3001, endpoints["api-http"].Port);
        Assert.Equal(3000, endpoints["api-https"].Port);

        var healthCheck = Assert.Single(api.Annotations.OfType<HealthCheckAnnotation>());
        Assert.StartsWith("api_api-http_/health", healthCheck.Key, StringComparison.Ordinal);

        var waits = api.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                "flight-db",
                "passenger-db",
                "identity-db",
                "booking-db",
                "mongo",
                "eventstore",
                "rabbitmq",
            },
            waits
        );

        var gatewayWaits = appHost
            .GetProject("gateway")
            .Annotations.OfType<WaitAnnotation>()
            .Select(wait => wait.Resource.Name);
        Assert.Equal(["api"], gatewayWaits);
    }

    [Fact]
    public async Task monolith_topology_still_honours_a_cluster_override()
    {
        var configuration = new Dictionary<string, string?>(AppHostUnderTest.Monolith)
        {
            ["Gateway:Clusters:booking"] = "http://booking-service:80",
        };
        await using var appHost = await AppHostUnderTest.CreateAsync(configuration);
        var env = await appHost.GetEnvironmentAsync("gateway");

        Assert.Equal("http://booking-service:80", env[ClusterAddressKey("booking")]);

        foreach (var cluster in Clusters.Where(name => name != "booking"))
        {
            Assert.Equal("{api.bindings.api-http.url}", env[ClusterAddressKey(cluster)]);
        }
    }

    [Fact]
    public async Task gateway_is_wired_identically_in_both_topologies()
    {
        await using var microservices = await AppHostUnderTest.CreateAsync();
        await using var monolith = await AppHostUnderTest.CreateAsync(AppHostUnderTest.Monolith);

        static (string Name, string Scheme, int? Port)[] Endpoints(ProjectResource gateway) =>
            gateway
                .Annotations.OfType<EndpointAnnotation>()
                .Select(e => (e.Name, e.UriScheme, e.Port))
                .OrderBy(e => e.Name)
                .ToArray();

        Assert.Equal(Endpoints(microservices.GetProject("gateway")), Endpoints(monolith.GetProject("gateway")));
        Assert.Equal(
            microservices.GetProject("gateway").Annotations.OfType<HealthCheckAnnotation>().Single().Key,
            monolith.GetProject("gateway").Annotations.OfType<HealthCheckAnnotation>().Single().Key
        );
    }

    private static string ClusterAddressKey(string cluster) =>
        $"ReverseProxy__Clusters__{cluster}__Destinations__monolith__Address";

    /// <summary>
    /// Builds the AppHost with the given configuration and parks its entry point at <see cref="BeforeStartEvent"/>
    /// so the model can be inspected without the orchestrator starting (see <see cref="GatewayAppHostWiringTests"/>).
    /// </summary>
    private sealed class AppHostUnderTest : IAsyncDisposable
    {
        public static readonly Dictionary<string, string?> Monolith = new() { ["AppHost:Topology"] = "Monolith" };

        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DistributedApplication _app = null!;

        public DistributedApplicationModel Model => _app.Services.GetRequiredService<DistributedApplicationModel>();

        public IEnumerable<string> ProjectNames =>
            Model.Resources.OfType<ProjectResource>().Select(project => project.Name).Order();

        public IEnumerable<HealthCheckRegistration> HealthCheckRegistrations =>
            _app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        public static async Task<AppHostUnderTest> CreateAsync(Dictionary<string, string?>? configuration = null)
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                [],
                (_, settings) => settings.Configuration?.AddInMemoryCollection(configuration ?? [])
            );

            var appHost = new AppHostUnderTest();
            var reachedBeforeStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            builder.Eventing.Subscribe<BeforeStartEvent>(
                async (_, _) =>
                {
                    reachedBeforeStart.TrySetResult();
                    await appHost._resume.Task;
                }
            );

            appHost._app = await builder.BuildAsync();
            await reachedBeforeStart.Task.WaitAsync(TimeSpan.FromSeconds(30));

            return appHost;
        }

        public ProjectResource GetProject(string name) =>
            Assert.Single(Model.Resources.OfType<ProjectResource>(), resource => resource.Name == name);

        public async Task<Dictionary<string, string>> GetEnvironmentAsync(string name)
        {
            var configuration = await ExecutionConfigurationBuilder
                .Create(GetProject(name))
                .WithEnvironmentVariablesConfig()
                .BuildAsync(
                    new DistributedApplicationExecutionContext(
                        new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish)
                        {
                            ServiceProvider = _app.Services,
                        }
                    )
                );

            Assert.Null(configuration.Exception);

            return new Dictionary<string, string>(configuration.EnvironmentVariables);
        }

        public async ValueTask DisposeAsync()
        {
            _resume.TrySetResult();
            await _app.DisposeAsync();
        }
    }
}
