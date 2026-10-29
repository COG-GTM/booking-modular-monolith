namespace BuildingBlocks.MassTransit;

// Bound from the "MessagingOptions" section (env: MessagingOptions__Transport=RabbitMq|InMemory).
public class MessagingOptions
{
    public TransportType Transport { get; set; } = TransportType.RabbitMq;
}
