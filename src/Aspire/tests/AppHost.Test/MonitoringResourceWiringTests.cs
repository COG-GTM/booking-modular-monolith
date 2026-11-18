using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AppHost.Test;

public class MonitoringResourceWiringTests
{
    [Fact]
    public async Task Kibana_setup_uses_elasticsearch_image_and_shell_script()
    {
        var builder = await CreatePublishBuilderAsync();
        var setup = Assert.IsType<ContainerResource>(Resource(builder, "kibana-setup"));
        var image = Assert.Single(setup.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("docker.elastic.co/elasticsearch/elasticsearch", image.Image);
        Assert.Equal("8.17.0", image.Tag);
        Assert.Equal("/bin/sh", setup.Entrypoint);

        var context = new CommandLineArgsCallbackContext(new List<object>());
        foreach (var annotation in setup.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        Assert.Equal("/usr/local/bin/setup-kibana-system-password.sh", Assert.Single(context.Args));
        var mount = Assert.Single(setup.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("/usr/local/bin/setup-kibana-system-password.sh", mount.Target);
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.True(mount.IsReadOnly);
        var expected = Path.GetFullPath(
            Path.Combine(
                builder.AppHostDirectory,
                "../../../../deployments/configs/elasticsearch/setup-kibana-system-password.sh"
            )
        );
        Assert.Equal(expected, mount.Source);
        Assert.True(File.Exists(mount.Source), $"Missing setup script: {mount.Source}");
    }

    [Fact]
    public async Task Kibana_setup_receives_password_parameters_and_waits_for_elasticsearch()
    {
        var builder = await CreatePublishBuilderAsync();
        var elasticsearch = Assert.IsType<ElasticsearchResource>(Resource(builder, "elasticsearch"));
        var setup = Resource(builder, "kibana-setup");
        var environment = await GetEnvironmentAsync(builder, setup);
        Assert.Same(elasticsearch.PasswordParameter, environment["ELASTIC_PASSWORD"]);
        Assert.Same(Resource(builder, "kibana-system-password"), environment["KIBANA_SYSTEM_PASSWORD"]);
        var wait = Assert.Single(setup.Annotations.OfType<WaitAnnotation>());
        Assert.Same(elasticsearch, wait.Resource);
        Assert.Equal(WaitType.WaitUntilHealthy, wait.WaitType);
    }

    [Fact]
    public async Task Kibana_waits_for_successful_setup_completion()
    {
        var builder = await CreatePublishBuilderAsync();
        var wait = Assert.Single(
            Resource(builder, "kibana").Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource.Name == "kibana-setup"
        );
        Assert.Same(Resource(builder, "kibana-setup"), wait.Resource);
        Assert.Equal(WaitType.WaitForCompletion, wait.WaitType);
        Assert.Equal(0, wait.ExitCode);
    }

    [Theory]
    [InlineData("otel-collector")]
    [InlineData("grafana")]
    public async Task Elasticsearch_clients_receive_the_elasticsearch_password_parameter(string name)
    {
        var builder = await CreatePublishBuilderAsync();
        var elasticsearch = Assert.IsType<ElasticsearchResource>(Resource(builder, "elasticsearch"));
        var environment = await GetEnvironmentAsync(builder, Resource(builder, name));
        Assert.Same(elasticsearch.PasswordParameter, environment["ELASTICSEARCH_PASSWORD"]);
    }

    [Fact]
    public async Task Rabbitmq_uses_named_credentials_with_booking_username_default()
    {
        var builder = await CreatePublishBuilderAsync();
        var rabbitmq = Assert.IsType<RabbitMQServerResource>(Resource(builder, "rabbitmq"));
        Assert.Same(Resource(builder, "rabbitmq-username"), rabbitmq.UserNameParameter);
        Assert.Same(Resource(builder, "rabbitmq-password"), rabbitmq.PasswordParameter);
        Assert.Equal("booking", await rabbitmq.UserNameParameter!.GetValueAsync(default));
        var environment = await GetEnvironmentAsync(builder, rabbitmq);
        var username = Assert.IsType<ReferenceExpression>(environment["RABBITMQ_DEFAULT_USER"]);
        Assert.Same(rabbitmq.UserNameParameter, Assert.Single(username.ValueProviders));
        Assert.Same(rabbitmq.PasswordParameter, environment["RABBITMQ_DEFAULT_PASS"]);
    }

    [Theory]
    [InlineData("rabbitmq-password")]
    [InlineData("grafana-admin-password")]
    [InlineData("kibana-system-password")]
    public async Task Generated_password_defaults_require_22_characters_without_special_characters(string name)
    {
        var builder = await CreatePublishBuilderAsync();
        var parameter = Assert.IsType<ParameterResource>(Resource(builder, name));
        var generated = Assert.IsType<GenerateParameterDefault>(parameter.Default);
        Assert.True(parameter.Secret);
        Assert.Equal(22, generated.MinLength);
        Assert.False(generated.Special);
    }

    [Fact]
    public async Task Run_mode_monitoring_endpoints_are_internal()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        Assert.True(builder.ExecutionContext.IsRunMode);
        string[] names =
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
            "tempo",
            "loki",
        ];
        foreach (var name in names)
        {
            var endpoints = Resource(builder, name).Annotations.OfType<EndpointAnnotation>().ToList();
            Assert.NotEmpty(endpoints);
            Assert.All(endpoints, endpoint => Assert.False(endpoint.IsExternal, $"{name}:{endpoint.Name}"));
        }
    }

    private static Task<IDistributedApplicationTestingBuilder> CreatePublishBuilderAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--operation", "publish", "--publisher", "docker-compose", "--output-path", Path.GetTempPath()]
        );

    private static IResource Resource(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.Single(resource => resource.Name == name);

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(
        IDistributedApplicationTestingBuilder builder,
        IResource resource
    )
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }
}
