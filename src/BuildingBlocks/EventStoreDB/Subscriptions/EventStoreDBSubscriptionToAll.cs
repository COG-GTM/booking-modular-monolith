using System.Runtime.ExceptionServices;
using System.Text;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.Utils;
using EventStore.Client;
using Grpc.Core;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BuildingBlocks.EventStoreDB.Subscriptions;

public class EventStoreDBSubscriptionToAllOptions
{
    public string SubscriptionId { get; set; } = "default";

    public SubscriptionFilterOptions FilterOptions { get; set; } =
        new(EventTypeFilter.ExcludeSystemEvents());

    public Action<EventStoreClientOperationOptions>? ConfigureOperation { get; set; }
    public UserCredentials? Credentials { get; set; }
    public bool ResolveLinkTos { get; set; }
    public bool IgnoreDeserializationErrors { get; set; } = true;

    // Events that still fail after MaxHandlingAttempts are moved to the parked stream and skipped,
    // so one bad event can't block the subscription. Set to false to stop and resubscribe instead.
    public bool ParkFailedEvents { get; set; } = true;
    public int MaxHandlingAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}

public class EventStoreDBSubscriptionToAll
{
    private readonly IProjectionPublisher projectionPublisher;
    private readonly EventStoreClient eventStoreClient;
    private readonly IMediator _mediator;
    private readonly ISubscriptionCheckpointRepository checkpointRepository;
    private readonly ILogger<EventStoreDBSubscriptionToAll> logger;
    private EventStoreDBSubscriptionToAllOptions subscriptionOptions = default!;
    private string SubscriptionId => subscriptionOptions.SubscriptionId;
    private readonly object resubscribeLock = new();
    private CancellationToken cancellationToken;

    public EventStoreDBSubscriptionToAll(
        EventStoreClient eventStoreClient,
        IMediator mediator,
        IProjectionPublisher projectionPublisher,
        ISubscriptionCheckpointRepository checkpointRepository,
        ILogger<EventStoreDBSubscriptionToAll> logger
    )
    {
        this.projectionPublisher = projectionPublisher;
        this.eventStoreClient = eventStoreClient ?? throw new ArgumentNullException(nameof(eventStoreClient));
        _mediator = mediator;
        this.checkpointRepository =
            checkpointRepository ?? throw new ArgumentNullException(nameof(checkpointRepository));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SubscribeToAll(EventStoreDBSubscriptionToAllOptions subscriptionOptions, CancellationToken ct)
    {
        // see: https://github.com/dotnet/runtime/issues/36063
        await Task.Yield();

        this.subscriptionOptions = subscriptionOptions;
        cancellationToken = ct;

        logger.LogInformation("Subscription to all '{SubscriptionId}'", subscriptionOptions.SubscriptionId);

        var checkpoint = await checkpointRepository.Load(SubscriptionId, ct);

        await eventStoreClient.SubscribeToAllAsync(
            checkpoint == null ? FromAll.Start : FromAll.After(new Position(checkpoint.Value, checkpoint.Value)),
            HandleEvent,
            subscriptionOptions.ResolveLinkTos,
            HandleDrop,
            subscriptionOptions.FilterOptions,
            subscriptionOptions.Credentials,
            ct
        );

        logger.LogInformation("Subscription to all '{SubscriptionId}' started", SubscriptionId);
    }

    public const string ParkedEventType = "parked-event";

    public static string GetParkedStreamName(string subscriptionId) => $"parked_{subscriptionId}";

    private async Task HandleEvent(StreamSubscription subscription, ResolvedEvent resolvedEvent,
        CancellationToken ct)
    {
        if (IsEventWithEmptyData(resolvedEvent) || IsCheckpointEvent(resolvedEvent) || IsParkedEvent(resolvedEvent))
            return;

        StreamEvent? streamEvent;

        try
        {
            streamEvent = resolvedEvent.ToStreamEvent();
        }
        catch (System.Exception e) when (e is not OperationCanceledException)
        {
            await ParkOrThrow(resolvedEvent, "deserialization-failed", e, ct);
            return;
        }

        if (streamEvent == null)
        {
            // Unknown event type: either written by another module sharing the database
            // or not one of the registered event types of this application.
            logger.LogWarning("Couldn't deserialize event with id: {EventId} and type: {EventType}",
                resolvedEvent.Event.EventId, resolvedEvent.Event.EventType);

            if (!subscriptionOptions.IgnoreDeserializationErrors)
                throw new InvalidOperationException($"Unable to deserialize event {resolvedEvent.Event.EventType} with id: {resolvedEvent.Event.EventId}");
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // publish event to internal event bus
                await _mediator.Publish(streamEvent, ct);

                await projectionPublisher.PublishAsync(streamEvent, ct);

                break;
            }
            catch (System.Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Error consuming event {EventId} of type {EventType} (attempt {Attempt}/{MaxAttempts})",
                    resolvedEvent.Event.EventId, resolvedEvent.Event.EventType, attempt,
                    subscriptionOptions.MaxHandlingAttempts);

                if (attempt >= subscriptionOptions.MaxHandlingAttempts)
                {
                    await ParkOrThrow(resolvedEvent, "handling-failed", e, ct);
                    return;
                }

                await Task.Delay(subscriptionOptions.RetryDelay, ct);
            }
        }

        await checkpointRepository.Store(SubscriptionId, resolvedEvent.Event.Position.CommitPosition, ct);
    }

    private async Task ParkOrThrow(ResolvedEvent resolvedEvent, string reason, System.Exception exception,
        CancellationToken ct)
    {
        if (!subscriptionOptions.ParkFailedEvents)
            ExceptionDispatchInfo.Throw(exception);

        var metadata = new
        {
            SubscriptionId,
            Reason = reason,
            Error = exception.Message,
            OriginalEventId = resolvedEvent.Event.EventId.ToString(),
            OriginalEventType = resolvedEvent.Event.EventType,
            OriginalStreamId = resolvedEvent.Event.EventStreamId,
            OriginalStreamRevision = resolvedEvent.Event.EventNumber.ToUInt64(),
            OriginalCommitPosition = resolvedEvent.Event.Position.CommitPosition,
            ParkedAt = DateTime.UtcNow
        };

        await eventStoreClient.AppendToStreamAsync(
            GetParkedStreamName(SubscriptionId),
            StreamState.Any,
            new[]
            {
                new EventData(
                    Uuid.NewUuid(),
                    ParkedEventType,
                    resolvedEvent.Event.Data,
                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata)))
            },
            cancellationToken: ct);

        logger.LogError(exception,
            "Parked event {EventId} of type {EventType} from stream {StreamId} to {ParkedStream} ({Reason})",
            resolvedEvent.Event.EventId, resolvedEvent.Event.EventType, resolvedEvent.Event.EventStreamId,
            GetParkedStreamName(SubscriptionId), reason);

        await checkpointRepository.Store(SubscriptionId, resolvedEvent.Event.Position.CommitPosition, ct);
    }

    private void HandleDrop(StreamSubscription _, SubscriptionDroppedReason reason, System.Exception? exception)
    {
        logger.LogError(
            exception,
            "Subscription to all '{SubscriptionId}' dropped with '{Reason}'",
            SubscriptionId,
            reason
        );

        if (exception is RpcException { StatusCode: StatusCode.Cancelled })
            return;

        Resubscribe();
    }

    private void Resubscribe()
    {
        // You may consider adding a max resubscribe count if you want to fail process
        // instead of retrying until database is up
        while (true)
        {
            var resubscribed = false;
            try
            {
                Monitor.Enter(resubscribeLock);

                // No synchronization context is needed to disable synchronization context.
                // That enables running asynchronous method not causing deadlocks.
                // As this is a background process then we don't need to have async context here.
                using (NoSynchronizationContextScope.Enter())
                {
                    SubscribeToAll(subscriptionOptions, cancellationToken).Wait(cancellationToken);
                }

                resubscribed = true;
            }
            catch (System.Exception exception)
            {
                logger.LogWarning(exception,
                    "Failed to resubscribe to all '{SubscriptionId}' dropped with '{ExceptionMessage}{ExceptionStackTrace}'",
                    SubscriptionId, exception.Message, exception.StackTrace);
            }
            finally
            {
                Monitor.Exit(resubscribeLock);
            }

            if (resubscribed)
                break;

            // Sleep between reconnections to not flood the database or not kill the CPU with infinite loop
            // Randomness added to reduce the chance of multiple subscriptions trying to reconnect at the same time
            Thread.Sleep(1000 + new Random((int)DateTime.UtcNow.Ticks).Next(1000));
        }
    }

    private bool IsEventWithEmptyData(ResolvedEvent resolvedEvent)
    {
        if (resolvedEvent.Event.Data.Length != 0)
            return false;

        logger.LogInformation("Event without data received");
        return true;
    }

    private bool IsParkedEvent(ResolvedEvent resolvedEvent) =>
        resolvedEvent.Event.EventType == ParkedEventType;

    private bool IsCheckpointEvent(ResolvedEvent resolvedEvent)
    {
        if (resolvedEvent.Event.EventType != EventTypeMapper.ToName<CheckpointStored>())
            return false;

        logger.LogInformation("Checkpoint event - ignoring");
        return true;
    }
}