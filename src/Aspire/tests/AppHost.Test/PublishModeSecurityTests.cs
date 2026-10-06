using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AppHost.Test;

public class PublishModeSecurityTests
{
    private static readonly string[] WellKnownCredentials = ["postgres", "secret", "guest", "admin"];

    [Fact]
    public async Task Secret_parameters_do_not_have_hardcoded_defaults()
    {
        await using var app = await FrozenAppHost.CreateAsync(publish: true);

        var secrets = app.Resources.OfType<ParameterResource>().Where(parameter => parameter.Secret).ToList();

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
        await using var app = await FrozenAppHost.CreateAsync(publish: true);

        var grafana = app.Resources.Single(resource => resource.Name == "grafana");
        var environment = await app.GetEnvironmentAsync(grafana.Name);

        var password = Assert.Contains("GF_SECURITY_ADMIN_PASSWORD", environment);
        Assert.IsType<ParameterResource>(password);
    }

    [Fact]
    public async Task EventStore_is_secured_in_publish_mode()
    {
        await using var app = await FrozenAppHost.CreateAsync(publish: true);

        var eventstore = app.Resources.Single(resource => resource.Name == "eventstore");
        var environment = await app.GetEnvironmentAsync(eventstore.Name);

        Assert.Equal("False", Assert.Contains("EVENTSTORE_INSECURE", environment));
        Assert.Equal("False", Assert.Contains("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", environment));
        Assert.IsType<ParameterResource>(Assert.Contains("EVENTSTORE_DEFAULT_ADMIN_PASSWORD", environment));
        Assert.Contains("EVENTSTORE_CERTIFICATE_FILE", environment);
    }

    [Fact]
    public async Task Api_connects_to_EventStore_over_TLS_with_private_CA_in_publish_mode()
    {
        await using var app = await FrozenAppHost.CreateAsync(publish: true);

        var api = app.Resources.Single(resource => resource.Name == "api");
        var environment = await app.GetEnvironmentAsync(api.Name);

        var connectionString = Assert.IsType<ReferenceExpression>(Assert.Contains("ConnectionStrings__eventstore", environment));
        Assert.Contains("tls=true", connectionString.Format, StringComparison.Ordinal);
        Assert.Contains("tlsCaFile=/etc/eventstore/certs/ca/ca.crt", connectionString.Format, StringComparison.Ordinal);
        Assert.Contains(connectionString.ValueProviders, provider => provider is ParameterResource { Name: "eventstore-admin-password" });

        var caMount = Assert.Single(api.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("/etc/eventstore/certs/ca", caMount.Target);
        Assert.True(caMount.IsReadOnly);
    }

    [Fact]
    public async Task EventStore_stays_insecure_for_local_run_mode()
    {
        await using var app = await FrozenAppHost.CreateAsync(publish: false);

        var eventstore = app.Resources.Single(resource => resource.Name == "eventstore");
        var environment = await app.GetEnvironmentAsync(eventstore.Name);

        Assert.Equal("True", Assert.Contains("EVENTSTORE_INSECURE", environment));
    }

    [Theory]
    [InlineData("eventstore", "http")]
    [InlineData("rabbitmq", "management")]
    [InlineData("grafana", "http")]
    public async Task Management_endpoints_are_not_external(string resourceName, string endpointName)
    {
        await using var app = await FrozenAppHost.CreateAsync(publish: true);

        var resource = app.Resources.Single(resource => resource.Name == resourceName);
        var endpoint = resource.Annotations.OfType<EndpointAnnotation>().Single(annotation => annotation.Name == endpointName);

        Assert.False(endpoint.IsExternal, $"{resourceName}/{endpointName} must not be exposed publicly.");
    }

    [Fact]
    public async Task No_container_environment_contains_well_known_credentials()
    {
        await using var app = await FrozenAppHost.CreateAsync(publish: true);

        foreach (var resource in app.Resources.Where(resource => resource.Annotations.OfType<EnvironmentCallbackAnnotation>().Any()))
        {
            var environment = await app.GetEnvironmentAsync(resource.Name);

            foreach (var (key, value) in environment.Where(pair => pair.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)))
            {
                Assert.False(
                    value is string literal && WellKnownCredentials.Contains(literal, StringComparer.OrdinalIgnoreCase),
                    $"{resource.Name}:{key} uses a well-known literal password.");
            }
        }
    }
}
