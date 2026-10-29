using BuildingBlocks.HealthCheck;
using FluentAssertions;
using RabbitMQ.Client;
using Xunit;

namespace BuildingBlocks.Tests;

public class RabbitMqHealthUriBoundaryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void absent_connection_string_falls_back_to_all_explicit_options(string? connectionString)
    {
        var factory = new ConnectionFactory
        {
            Uri = RabbitMqHealthUri.Resolve(connectionString, "broker.internal", 5678, "tenant", "alice", "password"),
        };

        factory.HostName.Should().Be("broker.internal");
        factory.Port.Should().Be(5678);
        factory.VirtualHost.Should().Be("tenant");
        factory.UserName.Should().Be("alice");
        factory.Password.Should().Be("password");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void omitted_or_blank_virtual_host_uses_root(string? virtualHost)
    {
        var factory = new ConnectionFactory
        {
            Uri = RabbitMqHealthUri.Resolve(null, "broker.internal", null, virtualHost, "guest", "guest"),
        };

        factory.VirtualHost.Should().Be("/");
        factory.Port.Should().Be(5672);
    }

    [Theory]
    [InlineData("tenant/bookings")]
    [InlineData("/tenant")]
    [InlineData("tenant bookings")]
    [InlineData("tenant%2Fbookings")]
    [InlineData("tenant?#bookings")]
    public void virtual_host_round_trips_as_one_amqp_path_segment(string virtualHost)
    {
        var factory = new ConnectionFactory
        {
            Uri = RabbitMqHealthUri.Resolve(null, "broker.internal", null, virtualHost, "guest", "guest"),
        };

        factory.VirtualHost.Should().Be(virtualHost);
    }

    [Theory]
    [InlineData("user@tenant", "p@ss:/?#%")]
    [InlineData("user:name", "with spaces")]
    [InlineData("literal%40user", "literal%2Fpassword")]
    public void credentials_round_trip_without_becoming_uri_delimiters(string userName, string password)
    {
        var factory = new ConnectionFactory
        {
            Uri = RabbitMqHealthUri.Resolve(null, "broker.internal", 5678, "tenant", userName, password),
        };

        factory.HostName.Should().Be("broker.internal");
        factory.Port.Should().Be(5678);
        factory.VirtualHost.Should().Be("tenant");
        factory.UserName.Should().Be(userName);
        factory.Password.Should().Be(password);
    }

    [Fact]
    public void tls_connection_string_preserves_tls_credentials_and_encoded_virtual_host()
    {
        var factory = new ConnectionFactory
        {
            Uri = RabbitMqHealthUri.Resolve(
                "amqps://probe:p%40ss@secure-broker/tenant%2Fbookings",
                "ignored-host",
                5678,
                "ignored-vhost",
                "ignored-user",
                "ignored-password"
            ),
        };

        factory.HostName.Should().Be("secure-broker");
        factory.Port.Should().Be(5671);
        factory.Ssl.Enabled.Should().BeTrue();
        factory.VirtualHost.Should().Be("tenant/bookings");
        factory.UserName.Should().Be("probe");
        factory.Password.Should().Be("p@ss");
    }
}
