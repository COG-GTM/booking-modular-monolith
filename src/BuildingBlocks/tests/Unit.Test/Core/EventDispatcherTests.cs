using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Core;

public class EventDispatcherTests
{
    private readonly IIntegrationEventPublisher publisher = Substitute.For<IIntegrationEventPublisher>();
    private readonly IEventHeadersProvider headersProvider = Substitute.For<IEventHeadersProvider>();
    private readonly IEventMapper mapper = Substitute.For<IEventMapper>();

    private EventDispatcher CreateDispatcher(params IEventMapper[] mappers)
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new EventDispatcher(
            scopeFactory,
            mappers,
            NullLogger<EventDispatcher>.Instance,
            publisher,
            headersProvider);
    }

    [Fact]
    public async Task send_async_should_publish_mapped_integration_event_with_provided_headers()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());
        var integrationEvent = new FakeIntegrationEvent(domainEvent.Id);
        var headers = new Dictionary<string, object?> { ["CorrelationId"] = "test-correlation" };

        mapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);
        headersProvider.GetHeaders().Returns(headers);

        var dispatcher = CreateDispatcher(mapper);

        await dispatcher.SendAsync(new IDomainEvent[] { domainEvent }.ToList().AsReadOnly());

        await publisher.Received(1).PublishAsync(
            Arg.Is<MessageEnvelope>(envelope =>
                ReferenceEquals(envelope.Message, integrationEvent) &&
                Equals(envelope.Headers["CorrelationId"], "test-correlation")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_async_should_compose_multiple_event_mappers()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());
        var integrationEvent = new FakeIntegrationEvent(domainEvent.Id);

        var nonMatchingMapper = Substitute.For<IEventMapper>();
        nonMatchingMapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        mapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);
        headersProvider.GetHeaders().Returns(new Dictionary<string, object?>());

        var dispatcher = CreateDispatcher(nonMatchingMapper, mapper);

        await dispatcher.SendAsync(new IDomainEvent[] { domainEvent }.ToList().AsReadOnly());

        await publisher.Received(1).PublishAsync(
            Arg.Is<MessageEnvelope>(envelope => ReferenceEquals(envelope.Message, integrationEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_async_should_not_publish_when_no_mapper_matches()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());

        mapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        headersProvider.GetHeaders().Returns(new Dictionary<string, object?>());

        var dispatcher = CreateDispatcher(mapper);

        await dispatcher.SendAsync(new IDomainEvent[] { domainEvent }.ToList().AsReadOnly());

        await publisher.DidNotReceive().PublishAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_async_should_publish_integration_events_directly()
    {
        var integrationEvent = new FakeIntegrationEvent(Guid.NewGuid());
        headersProvider.GetHeaders().Returns(new Dictionary<string, object?>());

        var dispatcher = CreateDispatcher(mapper);

        await dispatcher.SendAsync(new IIntegrationEvent[] { integrationEvent }.ToList().AsReadOnly());

        await publisher.Received(1).PublishAsync(
            Arg.Is<MessageEnvelope>(envelope => ReferenceEquals(envelope.Message, integrationEvent)),
            Arg.Any<CancellationToken>());
        mapper.DidNotReceive().MapToIntegrationEvent(Arg.Any<IDomainEvent>());
    }

    [Fact]
    public async Task send_async_should_wrap_events_implementing_have_integration_event()
    {
        var domainEvent = new FakeWrappedDomainEvent(Guid.NewGuid());
        headersProvider.GetHeaders().Returns(new Dictionary<string, object?>());

        var dispatcher = CreateDispatcher(mapper);

        await dispatcher.SendAsync(new IDomainEvent[] { domainEvent }.ToList().AsReadOnly());

        await publisher.Received(1).PublishAsync(
            Arg.Is<MessageEnvelope>(envelope =>
                envelope.Message is IntegrationEventWrapper<FakeWrappedDomainEvent> &&
                ((IntegrationEventWrapper<FakeWrappedDomainEvent>)envelope.Message!).DomainEvent == domainEvent),
            Arg.Any<CancellationToken>());
        mapper.DidNotReceive().MapToIntegrationEvent(Arg.Any<IDomainEvent>());
    }

    [Fact]
    public async Task send_async_should_add_internal_messages_for_internal_command_type()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());
        var internalCommand = new FakeInternalCommand(domainEvent.Id);

        mapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        mapper.MapToInternalCommand(domainEvent).Returns(internalCommand);
        headersProvider.GetHeaders().Returns(new Dictionary<string, object?>());

        var dispatcher = CreateDispatcher(mapper);

        await dispatcher.SendAsync(
            new IDomainEvent[] { domainEvent }.ToList().AsReadOnly(),
            typeof(FakeInternalCommand));

        await publisher.Received(1).AddInternalMessageAsync(internalCommand, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_async_should_do_nothing_for_empty_event_list()
    {
        var dispatcher = CreateDispatcher(mapper);

        await dispatcher.SendAsync(Array.Empty<IDomainEvent>().ToList().AsReadOnly());

        await publisher.DidNotReceive().PublishAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        headersProvider.DidNotReceive().GetHeaders();
    }
}
