using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class RabbitMqHostConfigurationTests
{
    [Fact]
    public void connection_string_takes_precedence_over_rabbitmq_options()
    {
        var configuration = Configuration(
            ("ConnectionStrings:rabbitmq", "amqp://alice:secret@broker.internal:5673/tenant"),
            ("RabbitMqOptions:HostName", "options-host"),
            ("RabbitMqOptions:Port", "5678"),
            ("RabbitMqOptions:VirtualHost", "options-vhost"),
            ("RabbitMqOptions:UserName", "options-user"),
            ("RabbitMqOptions:Password", "options-password")
        );

        var settings = ConfigureHost(configuration);

        settings.Host.Should().Be("broker.internal");
        settings.Port.Should().Be(5673);
        settings.VirtualHost.Should().Be("tenant");
        settings.Username.Should().Be("alice");
        settings.Password.Should().Be("secret");
    }

    [Fact]
    public void rabbitmq_options_are_reflected_in_host_settings()
    {
        var configuration = Configuration(
            ("RabbitMqOptions:HostName", "broker.internal"),
            ("RabbitMqOptions:Port", "5673"),
            ("RabbitMqOptions:VirtualHost", "tenant"),
            ("RabbitMqOptions:UserName", "alice"),
            ("RabbitMqOptions:Password", "secret")
        );

        var settings = ConfigureHost(configuration);

        settings.Host.Should().Be("broker.internal");
        settings.Port.Should().Be(5673);
        settings.VirtualHost.Should().Be("tenant");
        settings.Username.Should().Be("alice");
        settings.Password.Should().Be("secret");
    }

    [Fact]
    public void host_name_defaults_to_port_5672_and_root_virtual_host()
    {
        var settings = ConfigureHost(Configuration(("RabbitMqOptions:HostName", "broker.internal")));

        settings.Host.Should().Be("broker.internal");
        settings.Port.Should().Be(5672);
        settings.VirtualHost.Should().Be("/");
    }

    [Fact]
    public void whitespace_virtual_host_defaults_to_root_virtual_host()
    {
        var settings = ConfigureHost(
            Configuration(("RabbitMqOptions:HostName", "broker.internal"), ("RabbitMqOptions:VirtualHost", "  "))
        );

        settings.VirtualHost.Should().Be("/");
    }

    [Fact]
    public void whitespace_transport_defaults_to_rabbitmq()
    {
        Extensions
            .ResolveTransport(Configuration(("MessagingOptions:Transport", "   ")))
            .Should()
            .Be(TransportType.RabbitMq);
    }

    private static RabbitMqHostSettings ConfigureHost(IConfiguration configuration)
    {
        var configurator = Substitute.For<IRabbitMqBusFactoryConfigurator>();
        Extensions.ConfigureRabbitMqHost(configurator, configuration);

        return configurator
            .ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<RabbitMqHostSettings>()
            .Single();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}
