namespace BuildingBlocks.MassTransit;

public record MessageBrokerOptions
{
    public const string SectionName = "MessageBroker";

    public TransportType TransportType { get; set; } = TransportType.InMemory;

    // Prefixes every receive endpoint (queue) name so each service binds its own queues.
    public string? ServiceName { get; set; }

    // Wraps every consumer with the persist-message inbox (deduplication on MessageId).
    public bool UseInbox { get; set; }

    public RabbitMqTransportOptions RabbitMq { get; set; } = new();
}

public class RabbitMqTransportOptions
{
    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}
