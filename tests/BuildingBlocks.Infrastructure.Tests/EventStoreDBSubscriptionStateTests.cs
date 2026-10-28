using BuildingBlocks.EventStoreDB.Subscriptions;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class EventStoreDBSubscriptionStateTests
{
    [Fact]
    public void TracksSubscriptionTransitions()
    {
        var state = new EventStoreDBSubscriptionState();

        Assert.False(state.IsSubscribed);
        Assert.Null(state.LastChangedAt);

        state.MarkSubscribed();

        Assert.True(state.IsSubscribed);
        var subscribedAt = state.LastChangedAt;
        Assert.NotNull(subscribedAt);

        state.MarkUnsubscribed();

        Assert.False(state.IsSubscribed);
        var unsubscribedAt = state.LastChangedAt;
        Assert.NotNull(unsubscribedAt);
        Assert.True(unsubscribedAt.Value >= subscribedAt.Value);
    }
}
