using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.TestBase;
using EventStore.Client;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Integration.Test.Booking.Features;

public class EventStoreSubscriptionRetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_retry_projection_failures_and_only_dead_letter_after_the_attempt_limit(bool keepsFailing)
    {
        await using var container = TestContainers.EventStoreTestContainer();
        await container.StartAsync();
        using var client = new EventStoreClient(EventStoreClientSettings.Create(container.GetConnectionString()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var checkpoints = new EventStoreDBSubscriptionCheckpointRepository(client);
        var publisher = Substitute.For<IProjectionPublisher>();
        var options = new EventStoreDBSubscriptionToAllOptions
        {
            SubscriptionId = "projection-retry",
            MaxEventProcessingAttempts = 2,
            MaxResubscribeAttempts = 1,
            ResubscribeBaseDelay = TimeSpan.FromMilliseconds(50),
            ResubscribeMaxDelay = TimeSpan.FromMilliseconds(100),
        };
        var attempts = 0;
        publisher
            .PublishAsync(Arg.Any<StreamEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var data = (ProjectionTestEvent)call.Arg<StreamEvent>().Data;
                if (data.NeedsRetry && (Interlocked.Increment(ref attempts) == 1 || keepsFailing))
                    return Task.FromException(new InvalidOperationException("Projection failed"));

                return Task.CompletedTask;
            });
        var subscription = new EventStoreDBSubscriptionToAll(
            client,
            Substitute.For<IMediator>(),
            publisher,
            checkpoints,
            NullLogger<EventStoreDBSubscriptionToAll>.Instance
        );
        const string stream = "projection-retry-source";
        var retryTarget = new ProjectionTestEvent(true).ToJsonEventData();
        var originalWrite = await client.AppendToStreamAsync(
            stream,
            StreamState.NoStream,
            new[] { retryTarget },
            cancellationToken: ct
        );
        var nextWrite = await client.AppendToStreamAsync(
            stream,
            StreamState.StreamExists,
            new[] { new ProjectionTestEvent(false).ToJsonEventData() },
            cancellationToken: ct
        );

        try
        {
            await subscription.SubscribeToAll(options, ct);

            var expectedCheckpoint = nextWrite.LogPosition.CommitPosition;
            while (await checkpoints.Load(options.SubscriptionId, ct) != expectedCheckpoint)
                await Task.Delay(50, ct);

            Volatile.Read(ref attempts).Should().Be(options.MaxEventProcessingAttempts);

            var deadLetters = client.ReadStreamAsync(
                Direction.Forwards,
                EventStoreDBSubscriptionToAll.GetDeadLetterStreamName(options.SubscriptionId),
                StreamPosition.Start,
                cancellationToken: ct
            );
            if (!keepsFailing)
            {
                (await deadLetters.ReadState).Should().Be(ReadState.StreamNotFound);
                return;
            }

            var saved = (await deadLetters.ToListAsync(ct)).Should().ContainSingle().Subject.Event;
            saved.EventType.Should().Be(retryTarget.Type);
            saved.Data.ToArray().Should().Equal(retryTarget.Data.ToArray());
            var metadata = JObject.Parse(Encoding.UTF8.GetString(saved.Metadata.Span));
            metadata.Value<string>("OriginalEventId").Should().Be(retryTarget.EventId.ToString());
            metadata.Value<string>("OriginalStreamId").Should().Be(stream);
            metadata.Value<ulong>("OriginalCommitPosition").Should().Be(originalWrite.LogPosition.CommitPosition);
            metadata.Value<string>("Error").Should().Contain("Projection failed");
        }
        finally
        {
            await timeout.CancelAsync();
        }
    }

    public record ProjectionTestEvent(bool NeedsRetry) : IEvent;
}
