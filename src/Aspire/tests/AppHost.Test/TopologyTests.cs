using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AppHost.Test;

public class TopologyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Services")]
    public async Task Services_are_isolated_and_discoverable(string? topology)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            topology is null ? [] : [$"--AppHost:Topology={topology}"]
        );
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(p => p.Name);
        Assert.All(
            builder.Resources.OfType<PostgresDatabaseResource>(),
            database => Assert.EndsWith("_service", database.DatabaseName, StringComparison.Ordinal)
        );
        Assert.Equal(["booking", "flight", "gateway", "identity", "passenger"], projects.Keys.Order().ToArray());
        foreach (var name in new[] { "identity", "flight", "passenger", "booking" })
        {
            var env = await Environment(builder, projects[name]);
            Assert.Equal($"{{{name}-service-db.connectionString}}", env[$"ConnectionStrings__{name}"]);
            Assert.Equal(env[$"ConnectionStrings__{name}"], env[$"ConnectionStrings__{name}-persist-message"]);
            Assert.Equal(
                $"{{postgres.connectionString}};Database={name}_service",
                env[$"PostgresOptions__ConnectionString__{char.ToUpperInvariant(name[0])}{name[1..]}"]
            );
            foreach (var other in new[] { "identity", "flight", "passenger", "booking" }.Where(n => n != name))
                Assert.DoesNotContain($"ConnectionStrings__{other}", env.Keys);
            Assert.Equal("http://localhost:5000", env["Jwt__Authority"]);
            Assert.Equal("{identity.bindings.http.url}/.well-known/openid-configuration", env["Jwt__MetadataAddress"]);
            Assert.Contains("ConnectionStrings__rabbitmq", env.Keys);
            Assert.Equal(name, env["ObservabilityOptions__ServiceName"]);
            Assert.Equal(
                "{otel-collector.bindings.otel-grpc.url}",
                env["ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint"]
            );
            Assert.Equal(name != "identity", env.ContainsKey("ConnectionStrings__mongo"));
            Assert.Equal(name == "booking", env.ContainsKey("ConnectionStrings__eventstore"));
            var endpoints = projects[name].Annotations.OfType<EndpointAnnotation>().ToArray();
            Assert.Equal(name is "flight" or "passenger" ? 2 : 1, endpoints.Length);
            Assert.All(endpoints, e => Assert.False(e.IsProxied));
        }
        var booking = await Environment(builder, projects["booking"]);
        Assert.Equal("{flight.bindings.Grpc.url}", booking["services__flight__grpc__0"]);
        Assert.Equal("{passenger.bindings.Grpc.url}", booking["services__passenger__grpc__0"]);
        Assert.Contains("EventStoreOptions__ConnectionString", booking.Keys);
        var gateway = await Environment(builder, projects["gateway"]);
        foreach (var name in new[] { "identity", "flight", "passenger", "booking", "monolith" })
        {
            var target = name == "monolith" ? "identity" : name;
            var endpoint = target == "identity" ? "http" : "Http";
            Assert.Equal(
                $"{{{target}.bindings.{endpoint}.url}}",
                gateway[$"ReverseProxy__Clusters__{name}__Destinations__primary__Address"]
            );
        }
    }

    [Fact]
    public async Task Monolith_keeps_api_and_gateway()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--AppHost:Topology=Monolith"]
        );
        Assert.Equal(
            ["api", "gateway"],
            builder.Resources.OfType<ProjectResource>().Select(p => p.Name).Order().ToArray()
        );
        Assert.Equal(
            ["booking", "flight", "identity", "passenger"],
            builder
                .Resources.OfType<PostgresDatabaseResource>()
                .Select(database => database.DatabaseName)
                .Order()
                .ToArray()
        );
        var api = builder.Resources.OfType<ProjectResource>().Single(project => project.Name == "api");
        var grafana = builder.Resources.Single(resource => resource.Name == "grafana");
        Assert.Equal(3005, grafana.Annotations.OfType<EndpointAnnotation>().Single().Port);
        var env = await Environment(builder, api);
        foreach (var name in new[] { "booking", "flight", "identity", "passenger" })
            Assert.Equal($"{{{name}-db.connectionString}}", env[$"ConnectionStrings__{name}"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task Invalid_topology_is_rejected(string topology)
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                [$"--AppHost:Topology={topology}"]
            );
        });
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
            p => p.Key,
            p => p.Value is IManifestExpressionProvider expression ? expression.ValueExpression : p.Value.ToString()!
        );
    }
}
