using System.Collections.Concurrent;
using Npgsql;

namespace BuildingBlocks.PersistMessageProcessor;

public sealed class PersistMessageWorkerStores
{
    private readonly ConcurrentDictionary<string, byte> _owners = new();

    public bool TryClaim(string connectionString) => _owners.TryAdd(StoreKey(connectionString), 0);

    public void Release(string connectionString) => _owners.TryRemove(StoreKey(connectionString), out _);

    private static string StoreKey(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{connection.Host?.ToLowerInvariant()}:{connection.Port}/{connection.Database ?? connection.Username}";
    }
}
