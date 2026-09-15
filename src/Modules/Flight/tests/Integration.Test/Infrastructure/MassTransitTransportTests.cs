using Api;
using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Infrastructure;

public class MassTransitTransportTests : FlightIntegrationTestBase
{
    public MassTransitTransportTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public void rabbitmq_options_should_be_bound_from_configuration()
    {
        // Act
        var options = Fixture.ServiceProvider.GetRequiredService<RabbitMqOptions>();

        // Assert
        options.Should().NotBeNull();
        options.HostName.Should().Be(Fixture.RabbitMqTestContainer.Hostname);
        options.UserName.Should().Be(TestContainers.RabbitMqContainerConfiguration.UserName);
        options.Password.Should().Be(TestContainers.RabbitMqContainerConfiguration.Password);
        options
            .Port.Should()
            .Be(Fixture.RabbitMqTestContainer.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.Port));
        options.ExchangeName.Should().Be("booking");
    }

    [Fact]
    public void bus_should_use_rabbitmq_transport_with_configured_host()
    {
        // Arrange
        var options = Fixture.ServiceProvider.GetRequiredService<RabbitMqOptions>();

        // Act
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        // Assert
        bus.Address.Scheme.Should().Be("rabbitmq");
        bus.Address.Host.Should().Be(options.HostName);
        bus.Address.Port.Should().Be(options.Port);
    }
}
