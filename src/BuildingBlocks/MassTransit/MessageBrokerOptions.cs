namespace BuildingBlocks.MassTransit;

public class MessageBrokerOptions
{
    public TransportType TransportType { get; set; } = TransportType.InMemory;

    // Prefix for receive-endpoint (queue) names so every service owns its own queues on a shared broker.
    public string? ServiceName { get; set; }

    // Wraps consumers with the PersistMessageProcessor inbox filter (idempotent consumption).
    public bool UseInbox { get; set; } = true;
}
