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
        Assert.DoesNotContain("ReverseProxy__Clusters__monolith__Destinations__primary__Address", gateway.Keys);
        foreach (var name in new[] { "identity", "flight", "passenger", "booking" })
        {
            var endpoint = name == "identity" ? "http" : "Http";
            Assert.Equal(
                $"{{{name}.bindings.{endpoint}.url}}",
                gateway[$"ReverseProxy__Clusters__{name}__Destinations__primary__Address"]
            );
        }
    }

    [Theory]
    [InlineData("Services", true)]
    [InlineData("Services", false)]
    public async Task Published_listeners_bind_all_interfaces_without_changing_local_bindings(
        string topology,
        bool publish
    )
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            publish ? [$"--AppHost:Topology={topology}", "--publisher", "manifest"] : [$"--AppHost:Topology={topology}"]
        );
        Assert.Equal(publish, builder.ExecutionContext.IsPublishMode);
        foreach (var project in builder.Resources.OfType<ProjectResource>())
        {
            var env = await Environment(builder, project);
            foreach (var endpoint in project.Annotations.OfType<EndpointAnnotation>())
            {
                var key = project.Name is "identity" or "gateway"
                    ? "Urls"
                    : $"Kestrel__Endpoints__{endpoint.Name}__Url";
                if (publish)
                {
                    Assert.Equal($"{endpoint.UriScheme}://0.0.0.0:{endpoint.TargetPort}", env[key]);
                    Assert.Equal(endpoint.Port, endpoint.TargetPort);
                    if (endpoint.Name == "Grpc")
                        Assert.Equal("Http2", env["Kestrel__Endpoints__Grpc__Protocols"]);
                }
                else if (env.TryGetValue(key, out var localUrl))
                    Assert.DoesNotContain("0.0.0.0", localUrl, StringComparison.Ordinal);
            }
        }
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

    [Fact]
    public async Task Monolith_topology_is_rejected()
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                ["--AppHost:Topology=Monolith"]
            );
        });

        Assert.NotNull(exception);
        Assert.Contains(
            ExceptionChain(exception).OfType<ArgumentException>(),
            argumentException =>
                argumentException.Message.Contains("Unknown AppHost:Topology 'Monolith'", StringComparison.Ordinal)
                && argumentException.Message.Contains("Expected Services.", StringComparison.Ordinal)
        );
    }

    private static IEnumerable<Exception> ExceptionChain(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
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
            p => p.Key,
            p => p.Value is IManifestExpressionProvider expression ? expression.ValueExpression : p.Value.ToString()!
        );
    }
}
