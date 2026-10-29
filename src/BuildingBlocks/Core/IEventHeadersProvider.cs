namespace BuildingBlocks.Core;

// Contributes headers (correlation id, user, ...) to every integration event envelope.
public interface IEventHeadersProvider
{
    IReadOnlyDictionary<string, object?> GetHeaders();
}
