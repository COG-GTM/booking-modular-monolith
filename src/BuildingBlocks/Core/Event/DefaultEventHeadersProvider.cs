namespace BuildingBlocks.Core.Event;

public sealed class DefaultEventHeadersProvider : IEventHeadersProvider
{
    public IDictionary<string, object?> GetHeaders() => new Dictionary<string, object?>();
}
