using Api;
using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Transport;

public class RabbitMqTransportTests : FlightIntegrationTestBase
{
    public RabbitMqTransportTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public void should_use_rabbitmq_transport_for_bus()
    {
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        bus.Address.Scheme.Should().Be("rabbitmq");
    }

    [Fact]
    public void should_resolve_rabbitmq_options_from_configuration()
    {
        var options = Fixture.ServiceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        options.HostName.Should().Be(Fixture.RabbitMqTestContainer.Hostname);
        options
            .Port.Should()
            .Be(Fixture.RabbitMqTestContainer.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.Port));
    }
}
