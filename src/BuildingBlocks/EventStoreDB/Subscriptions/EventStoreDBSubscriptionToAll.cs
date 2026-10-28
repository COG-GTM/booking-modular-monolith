using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.Utils;
using EventStore.Client;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.EventStoreDB.Subscriptions;

public class EventStoreDBSubscriptionToAllOptions
{
    public string SubscriptionId { get; set; } = "default";

    public SubscriptionFilterOptions FilterOptions { get; set; } = new(EventTypeFilter.ExcludeSystemEvents());

    public Action<EventStoreClientOperationOptions>? ConfigureOperation { get; set; }
    public UserCredentials? Credentials { get; set; }
    public bool ResolveLinkTos { get; set; }
    public bool IgnoreDeserializationErrors { get; set; } = true;
}

public class EventStoreDBSubscriptionToAll
{
    private readonly IProjectionPublisher projectionPublisher;
    private readonly EventStoreClient eventStoreClient;
    private readonly IMediator _mediator;
    private readonly ISubscriptionCheckpointRepository checkpointRepository;
    private readonly ILogger<EventStoreDBSubscriptionToAll> logger;
    private readonly EventStoreDBSubscriptionState subscriptionState;
    private readonly SemaphoreSlim resubscribeLock = new(1, 1);
    private EventStoreDBSubscriptionToAllOptions subscriptionOptions = default!;
    private StreamSubscription? subscription;
    private string SubscriptionId => subscriptionOptions.SubscriptionId;
    private CancellationToken cancellationToken;

    public EventStoreDBSubscriptionToAll(
        EventStoreClient eventStoreClient,
        IMediator mediator,
        IProjectionPublisher projectionPublisher,
        ISubscriptionCheckpointRepository checkpointRepository,
        ILogger<EventStoreDBSubscriptionToAll> logger,
        EventStoreDBSubscriptionState subscriptionState
    )
    {
        this.projectionPublisher = projectionPublisher;
        this.eventStoreClient = eventStoreClient ?? throw new ArgumentNullException(nameof(eventStoreClient));
        _mediator = mediator;
        this.checkpointRepository =
            checkpointRepository ?? throw new ArgumentNullException(nameof(checkpointRepository));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.subscriptionState = subscriptionState ?? throw new ArgumentNullException(nameof(subscriptionState));
    }

    public async Task SubscribeToAll(EventStoreDBSubscriptionToAllOptions subscriptionOptions, CancellationToken ct)
    {
        this.subscriptionOptions = subscriptionOptions;
        cancellationToken = ct;

        logger.LogInformation("Subscription to all '{SubscriptionId}'", subscriptionOptions.SubscriptionId);

        try
        {
            await resubscribeLock.WaitAsync(ct);
            try
            {
                await SubscribeWithRetry(ct);
            }
            finally
            {
                resubscribeLock.Release();
            }

            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
        finally
        {
            await resubscribeLock.WaitAsync();
            try
            {
                var currentSubscription = Interlocked.Exchange(ref subscription, null);
                if (currentSubscription is not null)
                    currentSubscription.Dispose();
            }
            finally
            {
                subscriptionState.MarkUnsubscribed();
                resubscribeLock.Release();
            }
        }
    }

    private async Task HandleEvent(StreamSubscription subscription, ResolvedEvent resolvedEvent, CancellationToken ct)
    {
        try
        {
            if (IsEventWithEmptyData(resolvedEvent) || IsCheckpointEvent(resolvedEvent))
                return;

            var streamEvent = resolvedEvent.ToStreamEvent();

            if (streamEvent == null)
            {
                // That can happen if we're sharing database between modules.
                // If we're subscribing to all and not filtering out events from other modules,
                // then we might get events that are from other module and we might not be able to deserialize them.
                // In that case it's safe to ignore deserialization error.
                // You may add more sophisticated logic checking if it should be ignored or not.
                logger.LogWarning("Couldn't deserialize event with id: {EventId}", resolvedEvent.Event.EventId);

                if (!subscriptionOptions.IgnoreDeserializationErrors)
                    throw new InvalidOperationException(
                        $"Unable to deserialize event {resolvedEvent.Event.EventType} with id: {resolvedEvent.Event.EventId}"
                    );
                return;
            }

            // publish event to internal event bus
            await _mediator.Publish(streamEvent, ct);

            await projectionPublisher.PublishAsync(streamEvent, ct);

            await checkpointRepository.Store(SubscriptionId, resolvedEvent.Event.Position.CommitPosition, ct);
        }
        catch (System.Exception e)
        {
            logger.LogError(
                "Error consuming message: {ExceptionMessage}{ExceptionStackTrace}",
                e.Message,
                e.StackTrace
            );
            // if you're fine with dropping some events instead of stopping subscription
            // then you can add some logic if error should be ignored
            throw;
        }
    }

    private void HandleDrop(
        StreamSubscription droppedSubscription,
        SubscriptionDroppedReason reason,
        System.Exception? exception
    )
    {
        subscriptionState.MarkUnsubscribed();

        if (cancellationToken.IsCancellationRequested || reason == SubscriptionDroppedReason.Disposed)
        {
            logger.LogInformation(
                exception,
                "Subscription to all '{SubscriptionId}' dropped with '{Reason}'",
                SubscriptionId,
                reason
            );
            return;
        }

        logger.LogWarning(
            exception,
            "Subscription to all '{SubscriptionId}' dropped with '{Reason}'",
            SubscriptionId,
            reason
        );
        _ = Task.Run(() => ResubscribeAsync(droppedSubscription, cancellationToken), CancellationToken.None);
    }

    private async Task SubscribeWithRetry(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                var checkpoint = await checkpointRepository.Load(SubscriptionId, ct);
                var currentSubscription = await eventStoreClient.SubscribeToAllAsync(
                    checkpoint == null
                        ? FromAll.Start
                        : FromAll.After(new Position(checkpoint.Value, checkpoint.Value)),
                    HandleEvent,
                    subscriptionOptions.ResolveLinkTos,
                    HandleDrop,
                    subscriptionOptions.FilterOptions,
                    subscriptionOptions.Credentials,
                    ct
                );

                Volatile.Write(ref subscription, currentSubscription);
                subscriptionState.MarkSubscribed();
                logger.LogInformation("Subscription to all '{SubscriptionId}' started", SubscriptionId);
                return;
            }
            catch (System.Exception exception) when (!ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(backoff.TotalMilliseconds * (0.8 + Random.Shared.NextDouble() * 0.4), 30_000)
                );
                logger.LogWarning(
                    exception,
                    "Failed to subscribe to all '{SubscriptionId}'. Retrying in {Delay}",
                    SubscriptionId,
                    delay
                );
                await Task.Delay(delay, ct);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task ResubscribeAsync(StreamSubscription droppedSubscription, CancellationToken ct)
    {
        try
        {
            await resubscribeLock.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(subscription, droppedSubscription))
                    return;

                Interlocked.CompareExchange(ref subscription, null, droppedSubscription);
                try
                {
                    droppedSubscription.Dispose();
                }
                catch (System.Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to dispose dropped subscription for '{SubscriptionId}'",
                        SubscriptionId
                    );
                }

                await SubscribeWithRetry(ct);
            }
            finally
            {
                resubscribeLock.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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
}
