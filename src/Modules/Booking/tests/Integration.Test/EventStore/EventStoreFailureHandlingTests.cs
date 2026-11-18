using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api;
using Booking.Data;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.TestBase;
using EventStore.Client;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Integration.Test.EventStore;

public class EventStoreFailureHandlingTests : BookingIntegrationTestBase
{
    private readonly EventStoreClient client;
    private readonly IMediator mediator = Substitute.For<IMediator>();
    private readonly IProjectionPublisher projections = Substitute.For<IProjectionPublisher>();
    private readonly ISubscriptionCheckpointRepository checkpoints =
        Substitute.For<ISubscriptionCheckpointRepository>();
    private readonly EventStoreDBSubscriptionToAllOptions options = new()
    {
        SubscriptionId = $"failure-test-{Guid.NewGuid():N}",
        RetryDelay = TimeSpan.Zero,
    };

    public EventStoreFailureHandlingTests(TestReadFixture<Program, BookingReadDbContext> fixture)
        : base(fixture)
    {
        client = fixture.ServiceProvider.GetRequiredService<EventStoreClient>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task transient_handler_failures_should_retry_then_checkpoint_without_parking(int failures)
    {
        var resolved = await WriteEvent();
        var calls = 0;
        projections
            .PublishAsync(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                ++calls <= failures
                    ? Task.FromException(new InvalidOperationException("transient"))
                    : Task.CompletedTask
            );

        await Handle(resolved);

        calls.Should().Be(failures + 1);
        await mediator.Received(failures + 1).Publish(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>());
        await checkpoints
            .Received(1)
            .Store(options.SubscriptionId, resolved.Event.Position.CommitPosition, Arg.Any<CancellationToken>());
        var parked = client.ReadStreamAsync(Direction.Forwards, ParkedStream, StreamPosition.Start);
        (await parked.ReadState).Should().Be(ReadState.StreamNotFound);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task exhausted_handler_should_park_original_payload_and_metadata(int maxAttempts)
    {
        options.MaxHandlingAttempts = maxAttempts;
        var resolved = await WriteEvent();
        var before = DateTime.UtcNow;
        projections
            .PublishAsync(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("permanent")));

        await Handle(resolved);

        await projections.Received(maxAttempts).PublishAsync(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>());
        var parked = (
            await client.ReadStreamAsync(Direction.Forwards, ParkedStream, StreamPosition.Start).ToListAsync()
        )
            .Should()
            .ContainSingle()
            .Subject.Event;
        parked.EventType.Should().Be(EventStoreDBSubscriptionToAll.ParkedEventType);
        parked.Data.ToArray().Should().Equal(resolved.Event.Data.ToArray());
        var metadata = JObject.Parse(Encoding.UTF8.GetString(parked.Metadata.Span));
        metadata.Value<string>("SubscriptionId").Should().Be(options.SubscriptionId);
        metadata.Value<string>("Reason").Should().Be("handling-failed");
        metadata.Value<string>("Error").Should().Be("permanent");
        metadata.Value<string>("OriginalEventId").Should().Be(resolved.Event.EventId.ToString());
        metadata.Value<string>("OriginalEventType").Should().Be(resolved.Event.EventType);
        metadata.Value<string>("OriginalStreamId").Should().Be(resolved.Event.EventStreamId);
        metadata.Value<ulong>("OriginalStreamRevision").Should().Be(resolved.Event.EventNumber.ToUInt64());
        metadata.Value<ulong>("OriginalCommitPosition").Should().Be(resolved.Event.Position.CommitPosition);
        metadata.Value<DateTime>("ParkedAt").Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        await checkpoints
            .Received(1)
            .Store(options.SubscriptionId, resolved.Event.Position.CommitPosition, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task disabled_parking_should_rethrow_original_handler_exception_without_checkpoint()
    {
        options.ParkFailedEvents = false;
        var resolved = await WriteEvent();
        var error = new InvalidOperationException("do not park");
        projections
            .PublishAsync(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(error));

        Func<Task> act = () => Handle(resolved);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(error);
        await projections
            .Received(options.MaxHandlingAttempts)
            .PublishAsync(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>());
        await checkpoints.DidNotReceiveWithAnyArgs().Store(default!, default, default);
        (await client.ReadStreamAsync(Direction.Forwards, ParkedStream, StreamPosition.Start).ReadState)
            .Should()
            .Be(ReadState.StreamNotFound);
    }

    [Fact]
    public async Task parked_entries_should_be_ignored_even_when_deserialization_errors_are_not_ignored()
    {
        options.IgnoreDeserializationErrors = false;
        options.ParkFailedEvents = false;
        var resolved = await WriteEvent(
            new EventData(
                Uuid.NewUuid(),
                EventStoreDBSubscriptionToAll.ParkedEventType,
                Encoding.UTF8.GetBytes("{invalid")
            )
        );

        await Handle(resolved);

        mediator.ReceivedCalls().Should().BeEmpty();
        projections.ReceivedCalls().Should().BeEmpty();
        checkpoints.ReceivedCalls().Should().BeEmpty();
    }

    private string ParkedStream => EventStoreDBSubscriptionToAll.GetParkedStreamName(options.SubscriptionId);

    private async Task<ResolvedEvent> WriteEvent(EventData? data = null)
    {
        var stream = $"failure-source-{Guid.NewGuid():N}";
        await client.AppendToStreamAsync(
            stream,
            StreamState.NoStream,
            new[] { data ?? new RetryEvent("payload").ToJsonEventData() }
        );
        return (await client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start).ToListAsync()).Single();
    }

    private Task Handle(ResolvedEvent resolved)
    {
        var subscription = new EventStoreDBSubscriptionToAll(
            client,
            mediator,
            projections,
            checkpoints,
            NullLogger<EventStoreDBSubscriptionToAll>.Instance
        );
        typeof(EventStoreDBSubscriptionToAll)
            .GetField("subscriptionOptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(subscription, options);
        return (Task)
            typeof(EventStoreDBSubscriptionToAll)
                .GetMethod("HandleEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(subscription, new object?[] { null, resolved, CancellationToken.None })!;
    }

    public record RetryEvent(string Value) : IEvent;
}
