using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class PersistMessageProcessorRegistrationTests
{
    private static WebApplicationBuilder CreateBuilder() =>
        WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "test" });

    [Fact]
    public void add_persist_message_processor_registers_the_outbox_backed_integration_event_publisher()
    {
        var builder = CreateBuilder();

        builder.AddPersistMessageProcessor();

        builder
            .Services.Where(d => d.ServiceType == typeof(IIntegrationEventPublisher))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(PersistMessageIntegrationEventPublisher)
                && d.Lifetime == ServiceLifetime.Scoped
            );
        builder
            .Services.Should()
            .Contain(d =>
                d.ServiceType == typeof(IPersistMessageProcessor)
                && d.ImplementationType == typeof(BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor)
            );
    }

    [Fact]
    public void add_persist_message_processor_keeps_a_publisher_that_was_registered_before_it()
    {
        var builder = CreateBuilder();
        var custom = new RecordingPublisher();
        builder.Services.AddSingleton<IIntegrationEventPublisher>(custom);

        builder.AddPersistMessageProcessor();

        builder
            .Services.Where(d => d.ServiceType == typeof(IIntegrationEventPublisher))
            .Should()
            .ContainSingle()
            .Which.ImplementationInstance.Should()
            .BeSameAs(custom);
    }
}
