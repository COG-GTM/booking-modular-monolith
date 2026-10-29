using BuildingBlocks.HealthCheck;
using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace BuildingBlocks.Tests;

public class RabbitMqHealthUriTests
{
    [Fact]
    public void uses_configured_port_virtual_host_and_credentials()
    {
        var factory = new ConnectionFactory { Uri = RabbitMqHealthUri.Resolve(null, "rabbit", 5673, "booking", "booking", "p@ss") };

        factory.HostName.Should().Be("rabbit");
        factory.Port.Should().Be(5673);
        factory.VirtualHost.Should().Be("booking");
        factory.UserName.Should().Be("booking");
        factory.Password.Should().Be("p@ss");
    }

    [Fact]
    public void defaults_to_5672_and_root_virtual_host()
    {
        var factory = new ConnectionFactory { Uri = RabbitMqHealthUri.Resolve(null, "localhost", null, "/", "guest", "guest") };

        factory.Port.Should().Be(5672);
        factory.VirtualHost.Should().Be("/");
    }

    [Fact]
    public void aspire_connection_string_wins_over_options()
    {
        var factory = new ConnectionFactory
        {
            Uri = RabbitMqHealthUri.Resolve("amqp://u:p@aspire-rabbit:31000", "localhost", 5672, "/", "guest", "guest"),
        };

        factory.HostName.Should().Be("aspire-rabbit");
        factory.Port.Should().Be(31000);
        factory.UserName.Should().Be("u");
    }
}
