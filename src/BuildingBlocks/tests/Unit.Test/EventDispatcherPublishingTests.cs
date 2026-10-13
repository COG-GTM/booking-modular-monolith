using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class EventDispatcherPublishingTests
{
    private static EventDispatcher CreateDispatcher(
        IEnumerable<IEventMapper> mappers,
        IIntegrationEventPublisher? publisher = null,
        IEventHeadersProvider? headersProvider = null
    )
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new EventDispatcher(
            scopeFactory,
            mappers,
            NullLogger<EventDispatcher>.Instance,
            publisher,
            headersProvider
        );
    }

    [Fact]
    public async Task integration_events_bypass_the_mappers_and_are_published_with_provider_headers()
    {
        var publisher = new RecordingPublisher();
        var headersProvider = Substitute.For<IEventHeadersProvider>();
        headersProvider.GetHeaders().Returns(_ => new Dictionary<string, object?> { ["CorrelationId"] = "corr-7" });
        var sut = CreateDispatcher(Array.Empty<IEventMapper>(), publisher, headersProvider);
        var integrationEvent = new FlightCreatedIntegrationEvent(Guid.NewGuid());

        await sut.SendAsync(new IIntegrationEvent[] { integrationEvent });

        var envelope = publisher.Published.Should().ContainSingle().Subject;
        envelope.Message.Should().BeSameAs(integrationEvent);
        envelope.Headers.Should().ContainKey("CorrelationId").WhoseValue.Should().Be("corr-7");
    }

    [Fact]
    public async Task headers_are_resolved_separately_for_every_published_event()
    {
        var publisher = new RecordingPublisher();
        var headersProvider = Substitute.For<IEventHeadersProvider>();
        headersProvider.GetHeaders().Returns(_ => new Dictionary<string, object?>());
        var sut = CreateDispatcher(new IEventMapper[] { new FlightMapper() }, publisher, headersProvider);

        await sut.SendAsync(
            new IDomainEvent[]
            {
                new FlightCreatedDomainEvent(Guid.NewGuid()),
                new FlightCreatedDomainEvent(Guid.NewGuid()),
            }
        );

        headersProvider.Received(2).GetHeaders();
        publisher.Published.Should().HaveCount(2);
        publisher.Published.Select(x => x.Headers).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task missing_headers_provider_falls_back_to_an_empty_header_set()
    {
        var publisher = new RecordingPublisher();
        var sut = CreateDispatcher(new IEventMapper[] { new FlightMapper() }, publisher);

        await sut.SendAsync(new FlightCreatedDomainEvent(Guid.NewGuid()));

        publisher.Published.Should().ContainSingle().Which.Headers.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task dispatcher_without_any_registered_mapper_publishes_nothing_and_does_not_need_a_publisher()
    {
        var sut = CreateDispatcher(Array.Empty<IEventMapper>());

        var act = () =>
            sut.SendAsync(
                new IDomainEvent[] { new FlightCreatedDomainEvent(Guid.NewGuid()) },
                typeof(IInternalCommand)
            );

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task internal_commands_without_a_publisher_fail_with_a_registration_hint()
    {
        var sut = CreateDispatcher(new IEventMapper[] { new BookingMapper(), new FlightMapper() });

        var act = () =>
            sut.SendAsync(
                new IDomainEvent[] { new FlightCreatedDomainEvent(Guid.NewGuid()) },
                typeof(IInternalCommand)
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IIntegrationEventPublisher*AddPersistMessageProcessor*");
    }

    [Fact]
    public async Task cancellation_token_is_forwarded_to_the_publisher()
    {
        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var sut = CreateDispatcher(new IEventMapper[] { new FlightMapper() }, publisher);
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await sut.SendAsync(
            new IDomainEvent[] { new FlightCreatedDomainEvent(Guid.NewGuid()) },
            typeof(IInternalCommand),
            token
        );

        await publisher.Received(1).PublishAsync(Arg.Any<MessageEnvelope>(), token);
        await publisher.Received(1).AddInternalMessageAsync(Arg.Any<CreateFlightReadModel>(), token);
    }

    [Fact]
    public async Task events_mapped_by_a_mapper_registered_later_in_the_sequence_are_still_published()
    {
        var publisher = new RecordingPublisher();
        var sut = CreateDispatcher(new IEventMapper[] { new FlightMapper(), new BookingMapper() }, publisher);
        var id = Guid.NewGuid();

        await sut.SendAsync(new BookingCreatedDomainEvent(id));

        publisher.Published.Should().ContainSingle().Which.Message.Should().Be(new BookingCreatedIntegrationEvent(id));
    }
}
