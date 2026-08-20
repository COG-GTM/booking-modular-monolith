namespace BuildingBlocks.Core.Event;

public class DefaultEventHeadersProvider : IEventHeadersProvider
{
    public IDictionary<string, object?> GetHeaders()
    {
        return new Dictionary<string, object?>();
    }
}
