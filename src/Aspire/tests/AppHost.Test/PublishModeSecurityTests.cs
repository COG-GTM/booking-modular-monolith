using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppHost.Test;

public class PublishModeSecurityTests
{
    private static readonly string[] WellKnownCredentials = ["postgres", "secret", "guest", "admin"];

    [Fact]
    public async Task Secret_parameters_do_not_have_hardcoded_defaults()
    {
        await using var app = await BuildAsync(publish: true);

        var secrets = Model(app).Resources.OfType<ParameterResource>().Where(parameter => parameter.Secret).ToList();

        Assert.NotEmpty(secrets);
        Assert.All(
            secrets,
            parameter =>
            {
                Assert.True(parameter.Default is GenerateParameterDefault, $"{parameter.Name} must not have a literal default value.");
            });
    }

    [Fact]
    public async Task Grafana_admin_password_is_not_a_literal()
    {
        await using var app = await BuildAsync(publish: true);

        var grafana = Model(app).Resources.Single(resource => resource.Name == "grafana");
        var environment = await GetEnvironmentAsync(grafana, publish: true);

        var password = Assert.Contains("GF_SECURITY_ADMIN_PASSWORD", environment);
        Assert.IsType<ParameterResource>(password);
    }

    [Fact]
    public async Task EventStore_is_secured_in_publish_mode()
    {
        await using var app = await BuildAsync(publish: true);

        var eventstore = Model(app).Resources.Single(resource => resource.Name == "eventstore");
        var environment = await GetEnvironmentAsync(eventstore, publish: true);

        Assert.Equal("False", Assert.Contains("EVENTSTORE_INSECURE", environment));
        Assert.Equal("False", Assert.Contains("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", environment));
        Assert.IsType<ParameterResource>(Assert.Contains("EVENTSTORE_DEFAULT_ADMIN_PASSWORD", environment));
        Assert.Contains("EVENTSTORE_CERTIFICATE_FILE", environment);
    }

    [Fact]
    public async Task EventStore_stays_insecure_for_local_run_mode()
    {
        await using var app = await BuildAsync(publish: false);

        var eventstore = Model(app).Resources.Single(resource => resource.Name == "eventstore");
        var environment = await GetEnvironmentAsync(eventstore, publish: false);

        Assert.Equal("True", Assert.Contains("EVENTSTORE_INSECURE", environment));
    }

    [Theory]
    [InlineData("eventstore", "http")]
    [InlineData("rabbitmq", "management")]
    [InlineData("grafana", "http")]
    public async Task Management_endpoints_are_not_external(string resourceName, string endpointName)
    {
        await using var app = await BuildAsync(publish: true);

        var resource = Model(app).Resources.Single(resource => resource.Name == resourceName);
        var endpoint = resource.Annotations.OfType<EndpointAnnotation>().Single(annotation => annotation.Name == endpointName);

        Assert.False(endpoint.IsExternal, $"{resourceName}/{endpointName} must not be exposed publicly.");
    }

    [Fact]
    public async Task No_container_environment_contains_well_known_credentials()
    {
        await using var app = await BuildAsync(publish: true);

        foreach (var resource in Model(app).Resources.Where(resource => resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Any()))
        {
            var environment = await GetEnvironmentAsync(resource, publish: true);

            foreach (var (key, value) in environment.Where(pair => pair.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)))
            {
                Assert.False(
                    value is string literal && WellKnownCredentials.Contains(literal, StringComparer.OrdinalIgnoreCase),
                    $"{resource.Name}:{key} uses a well-known literal password.");
            }
        }
    }

    private static async Task<DistributedApplication> BuildAsync(bool publish)
    {
        var args = publish ? new[] { "--operation", "publish", "--publisher", "docker-compose", "--output-path", Path.GetTempPath() } : [];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(args);
        return await builder.BuildAsync();
    }

    private static DistributedApplicationModel Model(DistributedApplication app) =>
        app.Services.GetRequiredService<DistributedApplicationModel>();

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(IResource resource, bool publish)
    {
        var executionContext = new DistributedApplicationExecutionContext(
            publish ? DistributedApplicationOperation.Publish : DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(executionContext, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
