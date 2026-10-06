using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

// A module's dispatcher must only ever write to that module's outbox (IPersistMessageProcessor<TModule>).
public class PerModuleEventDispatcherTests
{
    public sealed class OtherModuleRoot;

    public sealed record TestDomainEvent(Guid Id) : IDomainEvent;

    public sealed record TestIntegrationEvent(Guid Id) : IIntegrationEvent;

    public sealed record TestInternalCommand(Guid Id) : InternalCommand;

    private readonly IPersistMessageProcessor<FlightRoot> _flightProcessor = Substitute.For<
        IPersistMessageProcessor<FlightRoot>
    >();
    private readonly IPersistMessageProcessor<OtherModuleRoot> _otherProcessor = Substitute.For<
        IPersistMessageProcessor<OtherModuleRoot>
    >();
    private readonly IEventMapper _eventMapper = Substitute.For<IEventMapper>();

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddEventHeadersProvider();
        services.AddSingleton(_eventMapper);
        services.AddSingleton(Substitute.For<IEventHeadersProvider>());
        services.AddSingleton(_flightProcessor);
        services.AddSingleton(_otherProcessor);
        services.AddScoped<
            IIntegrationEventPublisher<FlightRoot>,
            PersistMessageIntegrationEventPublisher<FlightRoot>
        >();
        services.AddScoped<
            IIntegrationEventPublisher<OtherModuleRoot>,
            PersistMessageIntegrationEventPublisher<OtherModuleRoot>
        >();
        services.AddEventDispatcher<FlightRoot>();
        services.AddEventDispatcher<OtherModuleRoot>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void dispatchers_should_be_registered_per_module_only()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<IEventDispatcher<FlightRoot>>()
            .Should()
            .BeOfType<EventDispatcher<FlightRoot>>();
        scope
            .ServiceProvider.GetRequiredService<IEventDispatcher<OtherModuleRoot>>()
            .Should()
            .BeOfType<EventDispatcher<OtherModuleRoot>>();
        scope.ServiceProvider.GetService<IEventDispatcher>().Should().BeNull();
    }

    [Fact]
    public async Task domain_event_should_be_published_to_the_owning_module_outbox_only()
    {
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        var integrationEvent = new TestIntegrationEvent(domainEvent.Id);
        _eventMapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher<FlightRoot>>();

        await dispatcher.SendAsync(domainEvent);

        await _flightProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(envelope => ReferenceEquals(envelope.Message, integrationEvent)),
                Arg.Any<CancellationToken>()
            );
        await _otherProcessor.DidNotReceiveWithAnyArgs().PublishMessageAsync(default(MessageEnvelope)!, default);
        await _flightProcessor.DidNotReceiveWithAnyArgs().AddInternalMessageAsync(default(InternalCommand)!, default);
    }

    [Fact]
    public async Task internal_command_should_be_stored_in_the_owning_module_outbox_only()
    {
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        var internalCommand = new TestInternalCommand(domainEvent.Id);
        _eventMapper.MapToInternalCommand(domainEvent).Returns(internalCommand);

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher<OtherModuleRoot>>();

        await dispatcher.SendAsync(domainEvent, typeof(TestInternalCommand));

        await _otherProcessor.Received(1).AddInternalMessageAsync(internalCommand, Arg.Any<CancellationToken>());
        await _flightProcessor.DidNotReceiveWithAnyArgs().AddInternalMessageAsync(default(InternalCommand)!, default);
        await _flightProcessor.DidNotReceiveWithAnyArgs().PublishMessageAsync(default(MessageEnvelope)!, default);
    }

    [Fact]
    public async Task domain_event_without_integration_event_should_write_nothing_to_the_outbox()
    {
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        _eventMapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher<FlightRoot>>();

        await dispatcher.SendAsync(domainEvent);

        await _flightProcessor.DidNotReceiveWithAnyArgs().PublishMessageAsync(default(MessageEnvelope)!, default);
        await _otherProcessor.DidNotReceiveWithAnyArgs().PublishMessageAsync(default(MessageEnvelope)!, default);
    }
}
