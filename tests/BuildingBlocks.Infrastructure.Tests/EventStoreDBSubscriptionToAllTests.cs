using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BuildingBlocks.EventStoreDB.BackgroundWorkers;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class EventStoreDBSubscriptionToAllTests
{
    [Fact]
    public async Task CheckpointLoadFailureIsRetriedWithoutFaultingTheSubscription()
    {
        var checkpoints = new ThrowingCheckpointRepository();
        var subscription = CreateSubscription(checkpoints);
        using var cancellation = new CancellationTokenSource();
        var task = subscription.SubscribeToAll(new EventStoreDBSubscriptionToAllOptions(), cancellation.Token);

        try
        {
            await WaitForLoadCountAsync(checkpoints, 2, TimeSpan.FromSeconds(5));
            Assert.False(task.IsFaulted);
            Assert.False(task.IsCompleted);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(task);
        }
    }

    [Fact]
    public async Task CancellationStopsSubscriptionRetryWithinThreeSeconds()
    {
        var checkpoints = new ThrowingCheckpointRepository();
        var subscription = CreateSubscription(checkpoints);
        using var cancellation = new CancellationTokenSource();
        var task = subscription.SubscribeToAll(new EventStoreDBSubscriptionToAllOptions(), cancellation.Token);

        cancellation.Cancel();

        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(task, completed);
        var exception = await Record.ExceptionAsync(async () => await task);
        Assert.True(exception is null or OperationCanceledException, exception?.ToString());
    }

    [Fact]
    public async Task BackgroundWorkerStopsDuringSubscriptionRetry()
    {
        var checkpoints = new ThrowingCheckpointRepository();
        var subscription = CreateSubscription(checkpoints);
        using var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            ct => subscription.SubscribeToAll(new EventStoreDBSubscriptionToAllOptions(), ct)
        );
        await worker.StartAsync(CancellationToken.None);
        await WaitForLoadCountAsync(checkpoints, 2, TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Worker took {stopwatch.Elapsed} to stop.");
        Assert.NotNull(worker.ExecuteTask);
        Assert.False(worker.ExecuteTask.IsFaulted);
        Assert.Equal(TaskStatus.RanToCompletion, worker.ExecuteTask.Status);
    }

    [Fact]
    public void DropFromStaleSubscriptionDoesNotClearSubscribedState()
    {
        var state = new EventStoreDBSubscriptionState();
        state.MarkSubscribed();
        var subscription = CreateSubscription(new ThrowingCheckpointRepository(), state);
        var currentSubscription = CreateStreamSubscription();
        var staleSubscription = CreateStreamSubscription();

        InvokeHandleDrop(subscription, staleSubscription, currentSubscription);

        Assert.True(state.IsSubscribed);
    }

    [Fact]
    public void DropFromCurrentSubscriptionClearsSubscribedState()
    {
        var state = new EventStoreDBSubscriptionState();
        state.MarkSubscribed();
        var subscription = CreateSubscription(new ThrowingCheckpointRepository(), state);
        var currentSubscription = CreateStreamSubscription();

        InvokeHandleDrop(subscription, currentSubscription, currentSubscription);

        Assert.False(state.IsSubscribed);
    }

    private static EventStoreDBSubscriptionToAll CreateSubscription(
        ThrowingCheckpointRepository checkpoints,
        EventStoreDBSubscriptionState? subscriptionState = null
    ) =>
        new(
            new EventStoreClient(EventStoreClientSettings.Create("esdb://127.0.0.1:1?tls=false")),
            Substitute.For<IMediator>(),
            Substitute.For<IProjectionPublisher>(),
            checkpoints,
            NullLogger<EventStoreDBSubscriptionToAll>.Instance,
            subscriptionState ?? new EventStoreDBSubscriptionState()
        );

    private static StreamSubscription CreateStreamSubscription() =>
        (StreamSubscription)RuntimeHelpers.GetUninitializedObject(typeof(StreamSubscription));

    private static void InvokeHandleDrop(
        EventStoreDBSubscriptionToAll subscription,
        StreamSubscription droppedSubscription,
        StreamSubscription currentSubscription
    )
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(EventStoreDBSubscriptionToAll);
        type.GetField("subscriptionOptions", flags)!.SetValue(subscription, new EventStoreDBSubscriptionToAllOptions());
        type.GetField("subscription", flags)!.SetValue(subscription, currentSubscription);
        type.GetMethod("HandleDrop", flags)!
            .Invoke(
                subscription,
                new object?[] { droppedSubscription, SubscriptionDroppedReason.Disposed, null }
            );
    }

    private static async Task WaitForLoadCountAsync(
        ThrowingCheckpointRepository checkpoints,
        int expectedCount,
        TimeSpan timeout
    )
    {
        var stopwatch = Stopwatch.StartNew();
        while (checkpoints.LoadCount < expectedCount && stopwatch.Elapsed < timeout)
            await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.True(
            checkpoints.LoadCount >= expectedCount,
            $"Checkpoint Load was called {checkpoints.LoadCount} times within {timeout}."
        );
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) { }
    }

    private sealed class ThrowingCheckpointRepository : ISubscriptionCheckpointRepository
    {
        private int loadCount;

        public int LoadCount => Volatile.Read(ref loadCount);

        public ValueTask<ulong?> Load(string subscriptionId, CancellationToken ct)
        {
            Interlocked.Increment(ref loadCount);
            throw new InvalidOperationException("Checkpoint load failed.");
        }

        public ValueTask Store(string subscriptionId, ulong position, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
