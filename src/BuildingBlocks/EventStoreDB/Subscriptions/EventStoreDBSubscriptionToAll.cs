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

    /// <summary>
    /// How many times an event that deserializes but fails in a handler or projection is retried
    /// before it is moved to the dead-letter stream and skipped.
    /// </summary>
    public int MaxEventProcessingAttempts { get; set; } = 5;

    public TimeSpan ResubscribeBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ResubscribeMaxDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Consecutive resubscribe attempts without handling an event before the subscription gives up.
    /// Null retries until the database is reachable again.
    /// </summary>
    public int? MaxResubscribeAttempts { get; set; }
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
    private ulong? failingEventPosition;
    private int failingEventAttempts;
    private int consecutiveResubscribeAttempts;

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

    private async Task HandleEvent(StreamSubscription subscription, ResolvedEvent resolvedEvent,
        CancellationToken ct)
    {
        try
        {
            if (IsEventWithEmptyData(resolvedEvent) || IsCheckpointEvent(resolvedEvent) ||
                IsDeadLetterEvent(resolvedEvent))
                return;

            StreamEvent? streamEvent;
            try
            {
                streamEvent = resolvedEvent.ToStreamEvent();

                if (streamEvent is { Data: not INotification })
                    throw new InvalidOperationException(
                        $"Event type '{streamEvent.Data.GetType().FullName}' is not a notification");
            }
            catch (System.Exception e) when (e is not OperationCanceledException)
            {
                // A body that doesn't fit its type fails the same way on every delivery, so retrying can't help.
                await DeadLetterAndSkip(resolvedEvent, e, ct);
                return;
            }

            if (streamEvent == null)
            {
                // That can happen if we're sharing database between modules.
                // If we're subscribing to all and not filtering out events from other modules,
                // then we might get events that are from other module and we might not be able to deserialize them.
                // In that case it's safe to ignore deserialization error.
                // You may add more sophisticated logic checking if it should be ignored or not.
                logger.LogWarning("Couldn't deserialize event with id: {EventId}", resolvedEvent.Event.EventId);

                if (!subscriptionOptions.IgnoreDeserializationErrors)
                    throw new InvalidOperationException($"Unable to deserialize event {resolvedEvent.Event.EventType} with id: {resolvedEvent.Event.EventId}");
                return;
            }

            // publish event to internal event bus
            await _mediator.Publish(streamEvent, ct);

            await projectionPublisher.PublishAsync(streamEvent, ct);

            await checkpointRepository.Store(SubscriptionId, resolvedEvent.Event.Position.CommitPosition, ct);

            MarkProgress();
        }
        catch (System.Exception e) when (e is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError("Error consuming message: {ExceptionMessage}{ExceptionStackTrace}", e.Message,
                e.StackTrace);

            if (RegisterFailedAttempt(resolvedEvent) < subscriptionOptions.MaxEventProcessingAttempts)
                throw;

            await DeadLetterAndSkip(resolvedEvent, e, ct);
        }
    }

    private int RegisterFailedAttempt(ResolvedEvent resolvedEvent)
    {
        var position = resolvedEvent.Event.Position.CommitPosition;

        if (failingEventPosition != position)
        {
            failingEventPosition = position;
            failingEventAttempts = 0;
        }

        return ++failingEventAttempts;
    }

    private void MarkProgress()
    {
        failingEventPosition = null;
        failingEventAttempts = 0;
        Interlocked.Exchange(ref consecutiveResubscribeAttempts, 0);
    }

    private async Task DeadLetterAndSkip(ResolvedEvent resolvedEvent, System.Exception exception,
        CancellationToken ct)
    {
        var record = resolvedEvent.Event;

        logger.LogError(
            exception,
            "Moving event {EventId} of type '{EventType}' from stream '{StreamId}' at position {Position} to dead-letter stream '{DeadLetterStream}'",
            record.EventId,
            record.EventType,
            record.EventStreamId,
            record.Position.CommitPosition,
            DeadLetterStreamName);

        var metadata = new
        {
            SubscriptionId,
            OriginalEventId = record.EventId.ToString(),
            OriginalEventType = record.EventType,
            OriginalStreamId = record.EventStreamId,
            OriginalEventNumber = record.EventNumber.ToUInt64(),
            OriginalCommitPosition = record.Position.CommitPosition,
            Error = $"{exception.GetType().FullName}: {exception.Message}",
            DeadLetteredAt = DateTime.UtcNow
        };

        await eventStoreClient.AppendToStreamAsync(
            DeadLetterStreamName,
            StreamState.Any,
            new[]
            {
                new EventData(
                    Uuid.NewUuid(),
                    record.EventType,
                    record.Data,
                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata)),
                    record.ContentType)
            },
            cancellationToken: ct);

        await checkpointRepository.Store(SubscriptionId, record.Position.CommitPosition, ct);

        MarkProgress();
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
        while (!cancellationToken.IsCancellationRequested)
        {
            var attempt = Interlocked.Increment(ref consecutiveResubscribeAttempts);

            if (attempt > subscriptionOptions.MaxResubscribeAttempts)
            {
                logger.LogCritical(
                    "Subscription to all '{SubscriptionId}' stopped after {Attempts} resubscribe attempts without progress",
                    SubscriptionId, attempt - 1);
                return;
            }

            // Back off between attempts so a failing event or an unavailable database doesn't flood the
            // database and logs or spin the CPU. Randomness reduces the chance of multiple subscriptions
            // reconnecting at the same time.
            if (cancellationToken.WaitHandle.WaitOne(GetResubscribeDelay(attempt)))
                return;

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
        }
    }

    private TimeSpan GetResubscribeDelay(int attempt)
    {
        var baseDelay = subscriptionOptions.ResubscribeBaseDelay;
        var maxDelay = subscriptionOptions.ResubscribeMaxDelay;
        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 16));
        var jitter = Random.Shared.NextDouble() * baseDelay.TotalMilliseconds;

        return TimeSpan.FromMilliseconds(Math.Min(exponential, maxDelay.TotalMilliseconds) + jitter);
    }

    private bool IsEventWithEmptyData(ResolvedEvent resolvedEvent)
    {
        if (resolvedEvent.Event.Data.Length != 0)
            return false;

        logger.LogInformation("Event without data received");
        return true;
    }

    private bool IsCheckpointEvent(ResolvedEvent resolvedEvent)
    {
        if (resolvedEvent.Event.EventType != EventTypeMapper.ToName<CheckpointStored>())
            return false;

        logger.LogInformation("Checkpoint event - ignoring");
        return true;
    }

    private bool IsDeadLetterEvent(ResolvedEvent resolvedEvent) =>
        resolvedEvent.Event.EventStreamId == DeadLetterStreamName;

    private string DeadLetterStreamName => GetDeadLetterStreamName(SubscriptionId);

    public static string GetDeadLetterStreamName(string subscriptionId) => $"deadletter_{subscriptionId}";
}