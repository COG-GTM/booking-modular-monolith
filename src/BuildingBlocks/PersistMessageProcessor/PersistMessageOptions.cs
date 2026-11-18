namespace BuildingBlocks.PersistMessageProcessor;

public class PersistMessageOptions
{
    public int? Interval { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public string? ConnectionString { get; set; }
    public int MaxRetryCount { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
}