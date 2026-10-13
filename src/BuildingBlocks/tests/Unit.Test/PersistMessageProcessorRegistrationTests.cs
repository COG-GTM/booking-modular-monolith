using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class PersistMessageProcessorRegistrationTests
{
    private sealed class FlightModule;

    private sealed class PassengerModule;

    private static WebApplicationBuilder CreateBuilder() =>
        WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "test" });

    [Fact]
    public void add_persist_message_processor_registers_the_module_outbox_backed_integration_event_publisher()
    {
        var builder = CreateBuilder();

        builder.AddPersistMessageProcessor<FlightModule>("Flight");

        builder
            .Services.Should()
            .ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher<FlightModule>))
            .Which.Should()
            .Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(PersistMessageIntegrationEventPublisher<FlightModule>)
                && d.Lifetime == ServiceLifetime.Scoped
            );
        builder
            .Services.Should()
            .Contain(d =>
                d.ServiceType == typeof(IPersistMessageProcessor<FlightModule>)
                && d.ImplementationType == typeof(PersistMessageProcessor<FlightModule>)
            );
        builder
            .Services.Should()
            .Contain(d =>
                d.ServiceType == typeof(IEventDispatcher<FlightModule>)
                && d.ImplementationType == typeof(EventDispatcher<FlightModule>)
            );
    }

    [Fact]
    public void add_persist_message_processor_registers_no_shared_publisher_or_processor()
    {
        var builder = CreateBuilder();

        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");

        builder.Services.Should().NotContain(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        builder.Services.Should().NotContain(d => d.ServiceType == typeof(IPersistMessageProcessor));
        builder
            .Services.Should()
            .ContainSingle(d => d.ServiceType == typeof(IIntegrationEventPublisher<PassengerModule>));
    }
}
