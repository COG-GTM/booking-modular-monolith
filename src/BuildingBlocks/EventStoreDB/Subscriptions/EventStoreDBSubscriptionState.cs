namespace BuildingBlocks.EventStoreDB.Subscriptions;

public sealed class EventStoreDBSubscriptionState
{
    private readonly object sync = new();
    private bool isSubscribed;
    private DateTimeOffset? lastChangedAt;

    public bool IsSubscribed
    {
        get
        {
            lock (sync)
                return isSubscribed;
        }
    }

    public DateTimeOffset? LastChangedAt
    {
        get
        {
            lock (sync)
                return lastChangedAt;
        }
    }

    public void MarkSubscribed() => SetSubscribed(true);

    public void MarkUnsubscribed() => SetSubscribed(false);

    private void SetSubscribed(bool value)
    {
        lock (sync)
        {
            isSubscribed = value;
            lastChangedAt = DateTimeOffset.UtcNow;
        }
    }
}
