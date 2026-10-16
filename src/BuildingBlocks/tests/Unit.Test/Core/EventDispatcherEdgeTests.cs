using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.CoreTests;

public class EventDispatcherEdgeTests
{
    private sealed record CoreWrappedDomainEvent(Guid Id) : IDomainEvent, IHaveIntegrationEvent;

    private sealed record CorePlainDomainEvent(Guid Id) : IDomainEvent;

    private sealed record CoreMappedIntegrationEvent(Guid Id) : IIntegrationEvent;

    private sealed record CoreMappedInternalCommand(Guid Id) : IInternalCommand;

    public sealed class CoreTestModule;

    private static IServiceScopeFactory ScopeFactory() =>
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    private static EventDispatcher CreateDispatcher(
        IEnumerable<IEventMapper> mappers,
        IIntegrationEventPublisher? publisher
    ) => new(ScopeFactory(), mappers, NullLogger<EventDispatcher>.Instance, publisher, eventHeadersProvider: null);

    [Fact]
    public async Task domain_event_with_integration_event_is_wrapped_without_consulting_mappers()
    {
        var mapper = Substitute.For<IEventMapper>();
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var sut = CreateDispatcher([mapper], publisher);
        var domainEvent = new CoreWrappedDomainEvent(Guid.NewGuid());

        await sut.SendAsync(new IDomainEvent[] { domainEvent });

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<MessageEnvelope>(e =>
                    e.Message is IntegrationEventWrapper<CoreWrappedDomainEvent>
                    && ReferenceEquals(
                        ((IntegrationEventWrapper<CoreWrappedDomainEvent>)e.Message).DomainEvent,
                        domainEvent
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        mapper.DidNotReceive().MapToIntegrationEvent(Arg.Any<IDomainEvent>());
    }

    [Fact]
    public async Task send_async_with_empty_list_never_touches_the_publisher()
    {
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var sut = CreateDispatcher(Array.Empty<IEventMapper>(), publisher);

        await sut.SendAsync(new List<IDomainEvent>());

        await publisher.DidNotReceive().PublishAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        await publisher
            .DidNotReceive()
            .AddInternalMessageAsync(Arg.Any<IInternalCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_async_with_internal_command_type_publishes_event_and_adds_internal_command()
    {
        var domainEvent = new CorePlainDomainEvent(Guid.NewGuid());
        var integrationEvent = new CoreMappedIntegrationEvent(domainEvent.Id);
        var internalCommand = new CoreMappedInternalCommand(domainEvent.Id);
        var mapper = Substitute.For<IEventMapper>();
        mapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);
        mapper.MapToInternalCommand(domainEvent).Returns(internalCommand);
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var sut = CreateDispatcher([mapper], publisher);

        await sut.SendAsync(new IDomainEvent[] { domainEvent }, type: typeof(InternalCommand));

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, integrationEvent)),
                Arg.Any<CancellationToken>()
            );
        await publisher
            .Received(1)
            .AddInternalMessageAsync(
                Arg.Is<IInternalCommand>(c => ReferenceEquals(c, internalCommand)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task send_async_with_non_internal_command_type_only_publishes_the_event()
    {
        var domainEvent = new CorePlainDomainEvent(Guid.NewGuid());
        var integrationEvent = new CoreMappedIntegrationEvent(domainEvent.Id);
        var internalCommand = new CoreMappedInternalCommand(domainEvent.Id);
        var mapper = Substitute.For<IEventMapper>();
        mapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);
        mapper.MapToInternalCommand(domainEvent).Returns(internalCommand);
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var sut = CreateDispatcher([mapper], publisher);

        await sut.SendAsync(new IDomainEvent[] { domainEvent }, type: typeof(CorePlainDomainEvent));

        await publisher.Received(1).PublishAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        await publisher
            .DidNotReceive()
            .AddInternalMessageAsync(Arg.Any<IInternalCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task single_event_overload_behaves_like_the_list_overload()
    {
        var domainEvent = new CorePlainDomainEvent(Guid.NewGuid());
        var integrationEvent = new CoreMappedIntegrationEvent(domainEvent.Id);
        var mapper = Substitute.For<IEventMapper>();
        mapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var sut = CreateDispatcher([mapper], publisher);

        await sut.SendAsync(domainEvent);

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, integrationEvent)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task module_dispatcher_publishes_through_the_module_aware_publisher()
    {
        var domainEvent = new CoreWrappedDomainEvent(Guid.NewGuid());
        var publisher = Substitute.For<IIntegrationEventPublisher<CoreTestModule>>();
        var sut = new EventDispatcher<CoreTestModule>(
            ScopeFactory(),
            Array.Empty<IEventMapper>(),
            NullLogger<EventDispatcher>.Instance,
            publisher
        );

        await sut.SendAsync(new IDomainEvent[] { domainEvent });

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<MessageEnvelope>(e => e.Message is IntegrationEventWrapper<CoreWrappedDomainEvent>),
                Arg.Any<CancellationToken>()
            );
    }
}
