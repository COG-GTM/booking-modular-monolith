using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AppHost.Test;

public class MonitoringExposureSecurityTests
{
    private static readonly string[] MonitoringResources =
    [
        "rabbitmq",
        "jaeger-all-in-one",
        "zipkin-all-in-one",
        "otel-collector",
        "prometheus",
        "grafana",
        "node-exporter",
        "elasticsearch",
        "kibana",
    ];

    private static readonly string[] GeneratedSecretParameters =
    [
        "rabbitmq-password",
        "grafana-admin-password",
        "kibana-system-password",
    ];

    [Fact]
    public async Task Monitoring_and_admin_endpoints_are_not_published_to_the_host()
    {
        var builder = await CreatePublishBuilderAsync();

        var external = builder.Resources
            .Where(resource => MonitoringResources.Contains(resource.Name))
            .SelectMany(resource => resource.Annotations.OfType<EndpointAnnotation>()
                .Where(endpoint => endpoint.IsExternal)
                .Select(endpoint => $"{resource.Name}:{endpoint.Name}:{endpoint.Port}"))
            .ToList();

        Assert.Empty(external);
    }

    [Fact]
    public async Task Admin_passwords_are_generated_not_hardcoded()
    {
        var builder = await CreatePublishBuilderAsync();
        var parameters = builder.Resources.OfType<ParameterResource>().ToDictionary(parameter => parameter.Name);

        Assert.All(
            GeneratedSecretParameters,
            name =>
            {
                Assert.True(parameters.TryGetValue(name, out var parameter), $"missing parameter {name}");
                Assert.True(parameter!.Secret, $"{name} must be secret");
                Assert.IsType<GenerateParameterDefault>(parameter.Default);
            });

        Assert.NotEqual("guest", await parameters["rabbitmq-username"].GetValueAsync(default));
    }

    [Fact]
    public async Task Grafana_admin_password_comes_from_secret_parameter()
    {
        var builder = await CreatePublishBuilderAsync();

        var environment = await GetEnvironmentAsync(builder, "grafana");

        var password = Assert.IsType<ParameterResource>(environment["GF_SECURITY_ADMIN_PASSWORD"]);
        Assert.Equal("grafana-admin-password", password.Name);
    }

    [Fact]
    public async Task Elasticsearch_security_is_enabled_and_kibana_authenticates()
    {
        var builder = await CreatePublishBuilderAsync();

        var elasticsearch = await GetEnvironmentAsync(builder, "elasticsearch");
        var kibana = await GetEnvironmentAsync(builder, "kibana");

        Assert.Equal("true", elasticsearch["xpack.security.enabled"]);
        Assert.Equal("kibana_system", kibana["ELASTICSEARCH_USERNAME"]);
        var password = Assert.IsType<ParameterResource>(kibana["ELASTICSEARCH_PASSWORD"]);
        Assert.Equal("kibana-system-password", password.Name);
    }

    private static Task<IDistributedApplicationTestingBuilder> CreatePublishBuilderAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--operation", "publish", "--publisher", "docker-compose", "--output-path", Path.GetTempPath()]);

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(
        IDistributedApplicationTestingBuilder builder,
        string resourceName)
    {
        var resource = builder.Resources.Single(resource => resource.Name == resourceName);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
