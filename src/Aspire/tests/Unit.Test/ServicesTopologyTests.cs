using Aspire.Hosting.ApplicationModel;
using Xunit;
using static AppHost.Unit.Test.AppHostModel;

namespace AppHost.Unit.Test;

// Default topology: gateway in front of one host per module, each with only its own resources.
public class ServicesTopologyTests
{
    private static readonly string[] Services = ["identity", "flight", "passenger", "booking"];
    private const string IdentityHttpUrl = "{identity.bindings.http.url}";

    // Endpoint names keep each host's casing: "http" from a launch profile, "Http"/"Grpc" from Kestrel config.

    [Fact]
    public async Task gateway_and_four_service_hosts_are_registered_without_the_monolith()
    {
        await using var appHost = await BuildAsync();
        var projects = GetResources(appHost.App).OfType<ProjectResource>().Select(p => p.Name).Order();

        Assert.Equal(["booking", "flight", "gateway", "identity", "passenger"], projects);
    }

    [Fact]
    public async Task unknown_topology_fails_fast()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => BuildAsync(new() { ["AppHost:Topology"] = "Mesh" }));

        Assert.Contains("AppHost:Topology", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("identity", "identity_service")]
    [InlineData("flight", "flight_service")]
    [InlineData("passenger", "passenger_service")]
    public async Task each_service_gets_its_own_write_and_outbox_databases(string service, string database)
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, service);

        Assert.Equal(DatabaseName(appHost, $"{service}-service-db"), database);
        Assert.Equal($"{{{service}-service-db.connectionString}}", env[$"ConnectionStrings__{service}"]);
        AssertOutbox(appHost, env, service);
    }

    [Fact]
    public async Task booking_has_no_postgres_write_store_but_owns_its_outbox_and_eventstore()
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, "booking");

        Assert.DoesNotContain(GetResources(appHost.App), r => r.Name == "booking-service-db");
        Assert.False(env.ContainsKey("ConnectionStrings__booking"));
        AssertOutbox(appHost, env, "booking");
        Assert.Equal("{eventstore.connectionString}", env["ConnectionStrings__eventstore"]);
    }

    [Theory]
    [InlineData("flight", "Flight")]
    [InlineData("passenger", "Passenger")]
    [InlineData("booking", "Booking")]
    public async Task read_side_services_get_their_own_mongo_database(string service, string module)
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, service);
        var readDb = Assert.Single(
            GetResources(appHost.App).OfType<MongoDBDatabaseResource>(),
            r => r.Name == $"{service}-service-read"
        );

        Assert.Equal($"{service}_service_read", readDb.DatabaseName);
        Assert.Equal($"{{{service}-service-read.connectionString}}", env[$"ConnectionStrings__{service}-mongo"]);
        Assert.Equal($"{service}_service_read", env[$"MongoOptions__{module}__DatabaseName"]);
        Assert.False(env.ContainsKey("ConnectionStrings__mongo"));
        Assert.Equal("{mongo.connectionString}", env["MongoOptions__ConnectionString"]);
    }

    [Fact]
    public async Task services_never_see_another_modules_stores()
    {
        await using var appHost = await BuildAsync();

        foreach (var service in Services)
        {
            var env = await GetEnvironmentAsync(appHost.App, service);
            var foreign = env.Keys.Where(key =>
                key.StartsWith("ConnectionStrings__", StringComparison.Ordinal)
                && Services.Where(other => other != service).Any(other => key.Contains(other, StringComparison.Ordinal))
            );

            Assert.Empty(foreign);
            Assert.Equal(service == "booking", env.ContainsKey("ConnectionStrings__eventstore"));
        }
    }

    [Fact]
    public async Task every_service_uses_rabbitmq_and_waits_for_its_resources()
    {
        await using var appHost = await BuildAsync();

        foreach (var service in Services)
        {
            var env = await GetEnvironmentAsync(appHost.App, service);
            Assert.Equal("RabbitMq", env["MessageBroker__TransportType"]);
            Assert.Equal("{rabbitmq.connectionString}", env["ConnectionStrings__rabbitmq"]);

            var waits = GetProject(appHost.App, service)
                .Annotations.OfType<WaitAnnotation>()
                .Select(w => w.Resource.Name);
            Assert.Contains("rabbitmq", waits);
            Assert.Contains($"{service}-service-persist-message", waits);
        }
    }

    [Fact]
    public async Task identity_issues_tokens_and_every_validator_trusts_it()
    {
        await using var appHost = await BuildAsync();

        Assert.Equal(IdentityHttpUrl, (await GetEnvironmentAsync(appHost.App, "identity"))["AuthOptions__IssuerUri"]);

        foreach (var resource in Services.Append("gateway"))
        {
            var env = await GetEnvironmentAsync(appHost.App, resource);
            Assert.Equal(IdentityHttpUrl, env["Jwt__Authority"]);
        }
    }

    [Fact]
    public async Task booking_calls_flight_and_passenger_over_their_plain_grpc_endpoints()
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, "booking");

        Assert.Equal("{flight.bindings.grpc.url}", env["Grpc__FlightAddress"], ignoreCase: true);
        Assert.Equal("{passenger.bindings.grpc.url}", env["Grpc__PassengerAddress"], ignoreCase: true);
        Assert.Contains(
            env.Keys,
            key => key.StartsWith("services__flight__grpc__", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            env.Keys,
            key => key.StartsWith("services__passenger__grpc__", StringComparison.OrdinalIgnoreCase)
        );

        var waits = GetProject(appHost.App, "booking")
            .Annotations.OfType<WaitAnnotation>()
            .Select(w => w.Resource.Name);
        Assert.Contains("flight", waits);
        Assert.Contains("passenger", waits);
        Assert.Contains("eventstore", waits);
    }

    [Theory]
    [InlineData("identity", 5100)]
    [InlineData("flight", 5200)]
    [InlineData("passenger", 5300)]
    [InlineData("booking", 5400)]
    public async Task services_keep_their_adr_0008_http_ports(string service, int port)
    {
        await using var appHost = await BuildAsync();
        var http = Assert.Single(
            GetProject(appHost.App, service).Annotations.OfType<EndpointAnnotation>(),
            e => e.Name.Equals("http", StringComparison.OrdinalIgnoreCase)
        );

        Assert.Equal(port, http.Port);
    }

    [Theory]
    [InlineData("flight", 5201)]
    [InlineData("passenger", 5301)]
    public async Task grpc_endpoints_are_http2_on_their_adr_0008_ports(string service, int port)
    {
        await using var appHost = await BuildAsync();
        var grpc = Assert.Single(
            GetProject(appHost.App, service).Annotations.OfType<EndpointAnnotation>(),
            e => e.Name.Equals("grpc", StringComparison.OrdinalIgnoreCase)
        );

        Assert.Equal(port, grpc.Port);
        Assert.Equal("http2", grpc.Transport);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    public async Task gateway_routes_each_module_cluster_to_its_service(string service)
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal($"{{{service}.bindings.http.url}}", env[ClusterAddressKey(service)], ignoreCase: true);
        Assert.Contains(
            env.Keys,
            key => key.StartsWith($"services__{service}__http__", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            GetProject(appHost.App, "gateway").Annotations.OfType<WaitAnnotation>(),
            wait => wait.Resource.Name == service
        );
    }

    [Fact]
    public async Task gateway_fallback_cluster_goes_to_identity_and_can_be_overridden()
    {
        await using (var appHost = await BuildAsync())
        {
            var env = await GetEnvironmentAsync(appHost.App, "gateway");
            Assert.Equal(IdentityHttpUrl, env[ClusterAddressKey("monolith")]);
        }

        await using (var appHost = await BuildAsync(new() { ["Gateway:Clusters:flight"] = "http://localhost:3001" }))
        {
            var env = await GetEnvironmentAsync(appHost.App, "gateway");
            Assert.Equal("http://localhost:3001", env[ClusterAddressKey("flight")]);
            Assert.Equal("{passenger.bindings.http.url}", env[ClusterAddressKey("passenger")], ignoreCase: true);
        }
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    public async Task dashboard_health_comes_from_each_services_health_endpoint(string service)
    {
        await using var appHost = await BuildAsync();

        Assert.Contains(
            GetProject(appHost.App, service).Annotations.OfType<HealthCheckAnnotation>(),
            check => check.Key.Contains(service, StringComparison.Ordinal)
        );
    }

    private static void AssertOutbox(AppHostUnderTest appHost, Dictionary<string, string> env, string service)
    {
        Assert.Equal($"{service}_service_persist_message", DatabaseName(appHost, $"{service}-service-persist-message"));
        Assert.Equal(
            $"{{{service}-service-persist-message.connectionString}}",
            env[$"ConnectionStrings__{service}-persist-message"]
        );
    }

    private static string DatabaseName(AppHostUnderTest appHost, string resource) =>
        Assert
            .Single(GetResources(appHost.App).OfType<PostgresDatabaseResource>(), r => r.Name == resource)
            .DatabaseName;
}
