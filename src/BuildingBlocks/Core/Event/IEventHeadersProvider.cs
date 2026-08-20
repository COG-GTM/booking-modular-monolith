namespace BuildingBlocks.Core.Event;

public interface IEventHeadersProvider
{
    IDictionary<string, object?> GetHeaders();
}
