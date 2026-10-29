using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AppHost.Test;

public class TopologyWiringTests
{
    [Fact]
    public async Task Services_topology_wires_expected_projects_endpoints_and_environment()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(project => project.Name);

        Assert.Equal(["booking", "flight", "gateway", "identity", "passenger"], projects.Keys.Order().ToArray());

        AssertEndpoints(projects["identity"], ("http", 5103, "http"));
        AssertEndpoints(projects["flight"], ("Http", 5107, "http"), ("Grpc", 5106, "http"));
        AssertEndpoints(projects["passenger"], ("Http", 5102, "http"), ("Grpc", 5202, "http"));
        AssertEndpoints(projects["booking"], ("Http", 5004, "http"));
        AssertEndpoints(projects["gateway"], ("http", 5000, "http"));

        var identity = await Environment(builder, projects["identity"]);
        var flight = await Environment(builder, projects["flight"]);
        var passenger = await Environment(builder, projects["passenger"]);
        var booking = await Environment(builder, projects["booking"]);
        var gateway = await Environment(builder, projects["gateway"]);

        Assert.Equal("Http2", flight["Kestrel__Endpoints__Grpc__Protocols"]);
        Assert.Equal("Http2", passenger["Kestrel__Endpoints__Grpc__Protocols"]);
        Assert.DoesNotContain("Kestrel__Endpoints__Grpc__Protocols", identity.Keys);
        Assert.DoesNotContain("Kestrel__Endpoints__Grpc__Protocols", booking.Keys);
        Assert.DoesNotContain("Kestrel__Endpoints__Grpc__Protocols", gateway.Keys);

        Assert.Equal("http://localhost:5000", identity["AuthOptions__IssuerUri"]);
        Assert.Equal("http://localhost:5000", gateway["Jwt__Authority"]);
        Assert.Equal("{identity.bindings.http.url}/.well-known/openid-configuration", gateway["Jwt__MetadataAddress"]);

        foreach (
            var (name, environment) in new[]
            {
                ("identity", identity),
                ("flight", flight),
                ("passenger", passenger),
                ("booking", booking),
                ("gateway", gateway),
            }
        )
            Assert.Equal(name, environment["ObservabilityOptions__ServiceName"]);

        foreach (
            var (name, environment) in new[] { ("flight", flight), ("passenger", passenger), ("booking", booking) }
        )
            Assert.Equal(
                builder.Resources.OfType<MongoDBServerResource>().Single().ConnectionStringExpression.ValueExpression,
                environment["MongoOptions__ConnectionString"]
            );

        Assert.DoesNotContain("MongoOptions__ConnectionString", identity.Keys);
        Assert.Equal(
            builder
                .Resources.OfType<IResourceWithConnectionString>()
                .Single(resource => resource.Name == "eventstore")
                .ConnectionStringExpression.ValueExpression,
            booking["EventStoreOptions__ConnectionString"]
        );
        Assert.DoesNotContain("EventStoreOptions__ConnectionString", identity.Keys);
        Assert.DoesNotContain("EventStoreOptions__ConnectionString", flight.Keys);
        Assert.DoesNotContain("EventStoreOptions__ConnectionString", passenger.Keys);
        Assert.DoesNotContain("EventStoreOptions__ConnectionString", gateway.Keys);
        Assert.DoesNotContain("MongoOptions__ConnectionString", gateway.Keys);
    }

    [Fact]
    public async Task Services_topology_has_expected_waits_health_checks_and_resources()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(project => project.Name);

        var serviceDatabases = new[]
        {
            "identity-service-db",
            "flight-service-db",
            "passenger-service-db",
            "booking-service-db",
        };
        AssertWaits(
            projects["identity"],
            ["identity-service-db", "rabbitmq"],
            serviceDatabases.Except(["identity-service-db"]).Append("eventstore")
        );
        AssertWaits(
            projects["flight"],
            ["flight-service-db", "rabbitmq", "mongo"],
            serviceDatabases.Except(["flight-service-db"]).Append("eventstore")
        );
        AssertWaits(
            projects["passenger"],
            ["passenger-service-db", "rabbitmq", "mongo"],
            serviceDatabases.Except(["passenger-service-db"]).Append("eventstore")
        );
        AssertWaits(
            projects["booking"],
            ["booking-service-db", "rabbitmq", "mongo", "eventstore"],
            serviceDatabases.Except(["booking-service-db"])
        );
        AssertWaits(
            projects["gateway"],
            ["identity", "flight", "passenger", "booking"],
            serviceDatabases.Append("eventstore")
        );

        Assert.All(
            projects.Values,
            project => Assert.NotNull(project.Annotations.OfType<HealthCheckAnnotation>().SingleOrDefault())
        );

        var grafana = builder.Resources.Single(resource => resource.Name == "grafana");
        Assert.Equal(
            3000,
            grafana.Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == "http").Port
        );
        Assert.DoesNotContain(projects.Keys, name => name == "api");

        var databases = builder.Resources.OfType<PostgresDatabaseResource>().ToArray();
        Assert.Equal(4, databases.Length);
        Assert.All(databases, database => Assert.EndsWith("-service-db", database.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppHost_infrastructure_uses_expected_image_pins_and_postgres_arguments()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);

        var postgres = builder.Resources.Single(resource => resource.Name == "postgres");
        var postgresImage = postgres.Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("postgres", postgresImage.Image);
        Assert.Equal("16", postgresImage.Tag);

        var argumentList = new List<object>();
        var commandLineContext = new CommandLineArgsCallbackContext(argumentList, postgres, default);
        foreach (var callback in postgres.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await callback.Callback(commandLineContext);
        Assert.Contains("wal_level=logical", argumentList.Select(argument => argument.ToString()));
        Assert.Contains("max_prepared_transactions=100", argumentList.Select(argument => argument.ToString()));

        var tempo = builder.Resources.Single(resource => resource.Name == "tempo");
        Assert.Equal("2.6.1", tempo.Annotations.OfType<ContainerImageAnnotation>().Single().Tag);

        var elasticsearch = builder.Resources.Single(resource => resource.Name == "elasticsearch");
        var elasticsearchImage = elasticsearch.Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("docker.elastic.co", elasticsearchImage.Registry);
        Assert.Equal("elasticsearch/elasticsearch", elasticsearchImage.Image);
        Assert.Equal("8.17.0", elasticsearchImage.Tag);
    }

    [Fact]
    public async Task Monolith_topology_wires_expected_api_endpoints_environment_and_gateway()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--AppHost:Topology=Monolith"]
        );
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(project => project.Name);
        var api = projects["api"];
        var gateway = projects["gateway"];
        var apiEnvironment = await Environment(builder, api);
        var gatewayEnvironment = await Environment(builder, gateway);

        Assert.Equal(["api", "gateway"], projects.Keys.Order().ToArray());
        AssertEndpoints(api, ("Http", 3001, "http"), ("Https", 3000, "https"), ("Grpc", 3002, "http"));
        Assert.Equal("{api.bindings.Grpc.url}", apiEnvironment["services__flight__grpc__0"]);
        Assert.Equal("{api.bindings.Grpc.url}", apiEnvironment["services__passenger__grpc__0"]);
        Assert.Equal(
            "{api.bindings.Http.url}/.well-known/openid-configuration",
            apiEnvironment["Jwt__MetadataAddress"]
        );
        Assert.Equal("http://localhost:5000", apiEnvironment["AuthOptions__IssuerUri"]);
        Assert.Equal("http://localhost:5000", apiEnvironment["Jwt__Authority"]);

        foreach (var cluster in new[] { "identity", "flight", "passenger", "booking", "monolith" })
            Assert.Equal(
                "{api.bindings.Http.url}",
                gatewayEnvironment[$"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address"]
            );

        Assert.Equal(
            "{api.bindings.Http.url}/.well-known/openid-configuration",
            gatewayEnvironment["Jwt__MetadataAddress"]
        );
        Assert.Contains(
            gateway.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name),
            name => name == "api"
        );
    }

    [Theory]
    [InlineData("services", "booking", "flight", "gateway", "identity", "passenger")]
    [InlineData("MONOLITH", "api", "gateway")]
    public async Task Topology_value_is_case_insensitive(string topology, params string[] expectedProjects)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            [$"--AppHost:Topology={topology}"]
        );

        Assert.Equal(
            expectedProjects.Order().ToArray(),
            builder.Resources.OfType<ProjectResource>().Select(project => project.Name).Order().ToArray()
        );
    }

    [Fact]
    public async Task Unknown_topology_reports_the_argument_exception()
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                ["--AppHost:Topology=microservices"]
            );
        });

        Assert.NotNull(exception);
        Assert.Contains(
            ExceptionChain(exception).OfType<ArgumentException>(),
            argumentException =>
                argumentException.Message.Contains("Unknown AppHost:Topology 'microservices'", StringComparison.Ordinal)
        );
    }

    private static void AssertEndpoints(
        ProjectResource project,
        params (string Name, int Port, string Scheme)[] expected
    )
    {
        var actual = project.Annotations.OfType<EndpointAnnotation>().ToArray();

        Assert.Equal(
            expected.OrderBy(endpoint => endpoint.Name).ToArray(),
            actual
                .Select(endpoint => (endpoint.Name, endpoint.Port!.Value, endpoint.UriScheme))
                .OrderBy(endpoint => endpoint.Name)
                .ToArray()
        );
        Assert.All(actual, endpoint => Assert.False(endpoint.IsProxied));
    }

    private static void AssertWaits(
        ProjectResource project,
        IEnumerable<string> expected,
        IEnumerable<string> forbidden
    )
    {
        var actual = project.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name).ToArray();
        Assert.All(expected, name => Assert.Contains(name, actual));
        Assert.All(forbidden, name => Assert.DoesNotContain(name, actual));
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            yield return current;
    }

    private static async Task<Dictionary<string, string>> Environment(
        IDistributedApplicationTestingBuilder builder,
        ProjectResource project
    )
    {
        var values = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, project, values, default);
        var callbacks = project.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList();
        foreach (var callback in callbacks)
            await callback.Callback(context);
        return values.ToDictionary(
            pair => pair.Key,
            pair =>
                pair.Value is IManifestExpressionProvider expression
                    ? expression.ValueExpression
                    : pair.Value.ToString()!
        );
    }
}
