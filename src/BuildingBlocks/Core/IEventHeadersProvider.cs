namespace BuildingBlocks.Core;

public interface IEventHeadersProvider
{
    IDictionary<string, object?> GetHeaders();
}
