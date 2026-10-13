namespace BuildingBlocks.PersistMessageProcessor;

// Each module's store is configured under PersistMessageOptions:ConnectionString:<Module>
// (or the Aspire connection string "<module>-persist-message"); there is no shared store.
public class PersistMessageOptions
{
    public int? Interval { get; set; } = 30;
    public bool Enabled { get; set; } = true;
}
