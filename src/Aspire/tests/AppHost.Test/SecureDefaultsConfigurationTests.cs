using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppHost.Test;

public class SecureDefaultsConfigurationTests
{
    private const string EventStoreCertsTarget = "/etc/eventstore/certs";

    [Theory]
    [InlineData("pg-password")]
    [InlineData("mongo-password")]
    [InlineData("rabbitmq-password")]
    [InlineData("grafana-admin-password")]
    [InlineData("eventstore-admin-password")]
    public async Task Password_parameters_are_generated_secrets_without_special_characters(string parameterName)
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: true);

        var parameter = appHost.Parameter(parameterName);

        Assert.True(parameter.Secret, $"{parameterName} must be marked as a secret.");
        var generated = Assert.IsType<GenerateParameterDefault>(parameter.Default);
        Assert.Equal(22, generated.MinLength);
        Assert.False(generated.Special);
    }

    [Theory]
    [InlineData("pg-username", "postgres")]
    [InlineData("mongo-username", "root")]
    [InlineData("rabbitmq-username", "guest")]
    public async Task Username_parameters_are_plain_defaults(string parameterName, string expectedDefault)
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: true);

        var parameter = appHost.Parameter(parameterName);

        Assert.False(parameter.Secret, $"{parameterName} must not be marked as a secret.");
        Assert.Equal(expectedDefault, await parameter.GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EventStore_admin_password_parameter_only_exists_in_publish_mode()
    {
        await using var publishAppHost = await FrozenAppHost.CreateAsync(publish: true);
        await using var runAppHost = await FrozenAppHost.CreateAsync(publish: false);

        var publishParameters = publishAppHost
            .Resources.OfType<ParameterResource>()
            .Select(parameter => parameter.Name);
        var runParameters = runAppHost.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name);

        Assert.Contains("eventstore-admin-password", publishParameters);
        Assert.DoesNotContain("eventstore-admin-password", runParameters);
    }

    [Fact]
    public async Task EventStore_tls_environment_points_at_mounted_certificates_in_publish_mode()
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: true);

        var environment = await appHost.GetEnvironmentAsync("eventstore");

        Assert.Equal($"{EventStoreCertsTarget}/node/node.crt", environment["EVENTSTORE_CERTIFICATE_FILE"]);
        Assert.Equal($"{EventStoreCertsTarget}/node/node.key", environment["EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE"]);
        Assert.Equal($"{EventStoreCertsTarget}/ca", environment["EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH"]);

        var adminPassword = Assert.IsType<ParameterResource>(environment["EVENTSTORE_DEFAULT_ADMIN_PASSWORD"]);
        Assert.Equal("eventstore-admin-password", adminPassword.Name);
    }

    [Fact]
    public async Task EventStore_mounts_certificates_read_only_in_publish_mode()
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: true);

        var mounts = appHost.Resource("eventstore").Annotations.OfType<ContainerMountAnnotation>().ToList();
        var mount = Assert.Single(mounts, annotation => annotation.Target == EventStoreCertsTarget);

        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.True(mount.IsReadOnly, "certificate mount must be read-only.");
        var expectedSuffix = Path.Combine("deployments", "configs", "eventstore", "certs");
        var source = Path.GetFullPath(mount.Source!).TrimEnd(Path.DirectorySeparatorChar);
        Assert.EndsWith(expectedSuffix, source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventStore_does_not_mount_certificates_or_require_admin_password_in_run_mode()
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: false);

        var mounts = appHost.Resource("eventstore").Annotations.OfType<ContainerMountAnnotation>().ToList();
        var environment = await appHost.GetEnvironmentAsync("eventstore");

        Assert.DoesNotContain(mounts, annotation => annotation.Target == EventStoreCertsTarget);
        Assert.DoesNotContain("EVENTSTORE_DEFAULT_ADMIN_PASSWORD", environment);
        Assert.DoesNotContain("EVENTSTORE_CERTIFICATE_FILE", environment);
        Assert.Equal("True", environment["EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP"]);
    }

    [Fact]
    public async Task Api_eventstore_connection_string_targets_the_eventstore_endpoint_with_the_admin_secret_in_publish_mode()
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: true);

        var environment = await appHost.GetEnvironmentAsync("api");

        var connectionString = Assert.IsType<ReferenceExpression>(environment["ConnectionStrings__eventstore"]);
        Assert.StartsWith("esdb://admin:", connectionString.Format, StringComparison.Ordinal);
        Assert.DoesNotContain("tls=false", connectionString.Format, StringComparison.Ordinal);

        var parameters = connectionString.ValueProviders.OfType<ParameterResource>().ToList();
        var adminPassword = Assert.Single(parameters);
        Assert.Equal("eventstore-admin-password", adminPassword.Name);

        var endpoints = connectionString.ValueProviders.OfType<EndpointReferenceExpression>().ToList();
        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, endpoint => Assert.Equal("eventstore", endpoint.Endpoint.Resource.Name));
        Assert.Contains(endpoints, endpoint => endpoint.Property == EndpointProperty.Host);
        Assert.Contains(endpoints, endpoint => endpoint.Property == EndpointProperty.Port);
    }

    [Fact]
    public async Task Api_ca_mount_points_at_the_eventstore_certs_ca_directory_in_publish_mode()
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: true);

        var caMount = Assert.Single(appHost.Resource("api").Annotations.OfType<ContainerMountAnnotation>().ToList());

        Assert.Equal(ContainerMountType.BindMount, caMount.Type);
        var expectedSuffix = Path.Combine("deployments", "configs", "eventstore", "certs", "ca");
        Assert.EndsWith(expectedSuffix, caMount.Source!.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
        Assert.True(Path.IsPathRooted(caMount.Source), "CA mount source must be an absolute path.");
    }

    [Fact]
    public async Task Api_eventstore_connection_string_is_not_overridden_in_run_mode()
    {
        await using var appHost = await FrozenAppHost.CreateAsync(publish: false);

        var environment = await appHost.GetEnvironmentAsync("api");

        var connectionString = environment["ConnectionStrings__eventstore"];
        Assert.IsNotType<ReferenceExpression>(connectionString);
        Assert.IsType<ConnectionStringReference>(connectionString);
        Assert.Empty(appHost.Resource("api").Annotations.OfType<ContainerMountAnnotation>().ToList());
    }

    /// <summary>
    /// Builds the AppHost model and freezes it at <see cref="BeforeStartEvent"/>, before Aspire's orchestrator,
    /// publisher or DCP mutate resource annotations in the background. Disposing releases the gate.
    /// </summary>
    private sealed class FrozenAppHost : IAsyncDisposable
    {
        private readonly DistributedApplication _app;
        private readonly TaskCompletionSource _gate;
        private readonly DistributedApplicationExecutionContext _executionContext;

        private FrozenAppHost(DistributedApplication app, TaskCompletionSource gate, bool publish)
        {
            _app = app;
            _gate = gate;
            _executionContext = new DistributedApplicationExecutionContext(
                publish ? DistributedApplicationOperation.Publish : DistributedApplicationOperation.Run
            );
        }

        public IReadOnlyList<IResource> Resources =>
            _app.Services.GetRequiredService<DistributedApplicationModel>().Resources.ToList();

        public static async Task<FrozenAppHost> CreateAsync(bool publish)
        {
            var args = publish
                ? new[]
                {
                    "--operation",
                    "publish",
                    "--publisher",
                    "docker-compose",
                    "--output-path",
                    Path.GetTempPath(),
                }
                : [];
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(args);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var frozen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            builder.Eventing.Subscribe<BeforeStartEvent>(
                async (_, cancellationToken) =>
                {
                    frozen.TrySetResult();
                    await gate.Task.WaitAsync(cancellationToken);
                }
            );

            var app = await builder.BuildAsync();
            await frozen.Task.WaitAsync(TimeSpan.FromSeconds(30));

            return new FrozenAppHost(app, gate, publish);
        }

        public IResource Resource(string name) => Resources.Single(resource => resource.Name == name);

        public ParameterResource Parameter(string name) =>
            Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == name);

        public async Task<Dictionary<string, object>> GetEnvironmentAsync(string resourceName)
        {
            var resource = Resource(resourceName);
            var context = new EnvironmentCallbackContext(_executionContext, resource);

            foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList())
            {
                await annotation.Callback(context);
            }

            return context.EnvironmentVariables;
        }

        public async ValueTask DisposeAsync()
        {
            _gate.TrySetCanceled();
            await _app.DisposeAsync();
        }
    }
}
