namespace BuildingBlocks.PersistMessageProcessor;

public class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
