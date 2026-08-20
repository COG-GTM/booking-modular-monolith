using FlightService;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Messaging;

public class RabbitMqTransportTests : FlightIntegrationTestBase
{
    public RabbitMqTransportTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public void should_configure_bus_with_rabbitmq_transport()
    {
        // Arrange
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        // Assert
        bus.Address.Scheme.Should().Be("rabbitmq");
    }

    [Fact]
    public void should_bind_rabbitmq_options_from_configuration()
    {
        // Arrange
        var rabbitMqOptions = Fixture.ServiceProvider.GetRequiredService<RabbitMqOptions>();

        // Assert
        rabbitMqOptions.Should().NotBeNull();
        rabbitMqOptions.HostName.Should().NotBeNullOrEmpty();
        rabbitMqOptions.UserName.Should().NotBeNullOrEmpty();
        rabbitMqOptions.Password.Should().NotBeNullOrEmpty();
        rabbitMqOptions.Port.Should().NotBeNull();
        rabbitMqOptions.ExchangeName.Should().Be("booking");
    }

    [Fact]
    public async Task should_publish_message_through_rabbitmq_broker()
    {
        // Arrange
        var message = new FlightCreated(Guid.NewGuid());

        // Act
        await Fixture.Publish(message);

        // Assert
        (await Fixture.WaitForPublishing<FlightCreated>()).Should().Be(true);
    }
}
