using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class EventStoreDBSubscriptionReadinessStateTests
{
    [Fact]
    public async Task AddEventStoreDBSubscriptionToAllRegistersSubscriptionStateAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddEventStoreDBSubscriptionToAll();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(EventStoreDBSubscriptionState));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        await using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<EventStoreDBSubscriptionState>(),
            provider.GetRequiredService<EventStoreDBSubscriptionState>()
        );
    }

    [Fact]
    public async Task AddEventStoreDBSubscriptionToAllKeepsPreviouslyRegisteredSubscriptionState()
    {
        var existing = new EventStoreDBSubscriptionState();
        var services = new ServiceCollection().AddSingleton(existing);

        services.AddEventStoreDBSubscriptionToAll();
        services.AddEventStoreDBSubscriptionToAll();

        Assert.Single(services, d => d.ServiceType == typeof(EventStoreDBSubscriptionState));
        await using var provider = services.BuildServiceProvider();
        Assert.Same(existing, provider.GetRequiredService<EventStoreDBSubscriptionState>());
    }

    [Fact]
    public void ConstructorRejectsMissingSubscriptionState()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CreateSubscription(new FailingCheckpoints(), null!));

        Assert.Equal("subscriptionState", exception.ParamName);
    }

    [Fact]
    public async Task StateStaysUnsubscribedWhileSubscribeIsRetrying()
    {
        var state = new EventStoreDBSubscriptionState();
        var checkpoints = new FailingCheckpoints();
        var subscription = CreateSubscription(checkpoints, state);
        using var cancellation = new CancellationTokenSource();
        var task = subscription.SubscribeToAll(new EventStoreDBSubscriptionToAllOptions(), cancellation.Token);

        try
        {
            await WaitUntilAsync(() => checkpoints.LoadCount >= 2, TimeSpan.FromSeconds(5));
            Assert.False(state.IsSubscribed);
            Assert.Null(state.LastChangedAt);
        }
        finally
        {
            cancellation.Cancel();
            await Record.ExceptionAsync(async () => await task);
        }
    }

    [Fact]
    public async Task StoppingTheSubscriptionMarksStateUnsubscribed()
    {
        var state = new EventStoreDBSubscriptionState();
        state.MarkSubscribed();
        var subscribedAt = state.LastChangedAt!.Value;
        var subscription = CreateSubscription(new FailingCheckpoints(), state);
        using var cancellation = new CancellationTokenSource();
        var task = subscription.SubscribeToAll(new EventStoreDBSubscriptionToAllOptions(), cancellation.Token);

        cancellation.Cancel();
        var exception = await Record.ExceptionAsync(async () => await task);

        Assert.True(exception is null or OperationCanceledException, exception?.ToString());
        Assert.False(state.IsSubscribed);
        Assert.True(state.LastChangedAt >= subscribedAt);
    }

    private static EventStoreDBSubscriptionToAll CreateSubscription(
        ISubscriptionCheckpointRepository checkpoints,
        EventStoreDBSubscriptionState state
    ) =>
        new(
            new EventStoreClient(EventStoreClientSettings.Create("esdb://127.0.0.1:1?tls=false")),
            Substitute.For<IMediator>(),
            Substitute.For<IProjectionPublisher>(),
            checkpoints,
            NullLogger<EventStoreDBSubscriptionToAll>.Instance,
            state
        );

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.True(condition(), $"Condition was not met within {timeout}.");
    }

    private sealed class FailingCheckpoints : ISubscriptionCheckpointRepository
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
