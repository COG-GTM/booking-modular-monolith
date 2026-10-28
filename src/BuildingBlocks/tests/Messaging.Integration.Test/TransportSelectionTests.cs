using BuildingBlocks.MassTransit;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class TransportSelectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_service_name_for_endpoint_prefixes(string serviceName)
    {
        Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddCustomMassTransit(Environment("test"), Config(), serviceName)
        );
    }

    [Fact]
    public async Task Test_environment_defaults_to_the_in_memory_transport()
    {
        await using var provider = Build("test", Config());

        Assert.Equal("loopback", provider.GetRequiredService<IBus>().Address.Scheme);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task Non_test_environments_default_to_rabbitmq_without_any_messaging_settings(string environment)
    {
        await using var provider = Build(environment, Config());

        var address = provider.GetRequiredService<IBus>().Address;
        Assert.Equal("rabbitmq", address.Scheme);
        Assert.Equal("localhost", address.Host);
    }

    [Fact]
    public async Task Explicit_rabbitmq_transport_overrides_the_test_environment_default()
    {
        await using var provider = Build("test", Config(("Messaging:Transport", "RabbitMq")));

        Assert.Equal("rabbitmq", provider.GetRequiredService<IBus>().Address.Scheme);
    }

    [Fact]
    public async Task Aspire_connection_string_takes_precedence_over_rabbitmq_options()
    {
        await using var provider = Build(
            "Production",
            Config(
                ("ConnectionStrings:rabbitmq", "amqp://svc:secret@aspire-broker:5673/aspire-vhost"),
                ("RabbitMqOptions:HostName", "options-broker"),
                ("RabbitMqOptions:VirtualHost", "options-vhost")
            )
        );

        var address = provider.GetRequiredService<IBus>().Address;
        Assert.Equal("aspire-broker", address.Host);
        Assert.Equal(5673, address.Port);
        Assert.StartsWith("/aspire-vhost/", address.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rabbitmq_options_supply_host_port_and_virtual_host_without_a_connection_string()
    {
        await using var provider = Build(
            "Production",
            Config(
                ("RabbitMqOptions:HostName", "options-broker"),
                ("RabbitMqOptions:Port", "5674"),
                ("RabbitMqOptions:VirtualHost", "booking")
            )
        );

        var address = provider.GetRequiredService<IBus>().Address;
        Assert.Equal("options-broker", address.Host);
        Assert.Equal(5674, address.Port);
        Assert.StartsWith("/booking/", address.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Rabbitmq_options_default_to_the_local_compose_broker()
    {
        var options = new RabbitMqOptions();

        Assert.Equal("localhost", options.HostName);
        Assert.Equal(5672, options.Port);
        Assert.Equal("/", options.VirtualHost);
        Assert.Equal("guest", options.UserName);
        Assert.Equal("guest", options.Password);
    }

    [Fact]
    public async Task Endpoint_names_are_prefixed_with_the_service_name()
    {
        await using var provider = Build("test", Config(), "passenger", typeof(ProbeConsumer).Assembly);

        Assert.Equal(
            "passenger-probe",
            provider.GetRequiredService<IEndpointNameFormatter>().Consumer<ProbeConsumer>()
        );
    }

    [Fact]
    public void Consumers_are_registered_only_from_the_supplied_assemblies()
    {
        var withoutAssemblies = new ServiceCollection().AddLogging();
        withoutAssemblies.AddCustomMassTransit(Environment("test"), Config(), "publisher");
        var withAssembly = new ServiceCollection().AddLogging();
        withAssembly.AddCustomMassTransit(
            Environment("test"),
            Config(),
            "consumer",
            typeof(ProbeConsumer).Assembly,
            typeof(ProbeConsumer).Assembly
        );

        Assert.DoesNotContain(withoutAssemblies, x => x.ServiceType == typeof(ProbeConsumer));
        Assert.Single(withAssembly, x => x.ServiceType == typeof(ProbeConsumer));
    }

    private static ServiceProvider Build(
        string environment,
        IConfiguration configuration,
        string serviceName = "svc",
        params System.Reflection.Assembly[] assemblies
    )
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCustomMassTransit(Environment(environment), configuration, serviceName, assemblies);
        return services.BuildServiceProvider();
    }

    private static IWebHostEnvironment Environment(string name)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();
}
