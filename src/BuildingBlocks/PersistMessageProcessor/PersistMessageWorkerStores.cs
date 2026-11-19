using System.Collections.Concurrent;
using Npgsql;

namespace BuildingBlocks.PersistMessageProcessor;

public sealed class PersistMessageWorkerStores
{
    private readonly ConcurrentDictionary<string, byte> _owners = new();
    private readonly Dictionary<Type, string> _moduleStores = new();

    public PersistMessageWorkerStores() { }

    internal PersistMessageWorkerStores(IEnumerable<PersistMessageModuleStore> moduleStores)
    {
        foreach (var module in moduleStores)
            _moduleStores[module.ModuleType] = StoreKey(module.ConnectionString);
    }

    public bool CanProcessInternalCommand<TModule>(Type commandType) =>
        commandType.Assembly == typeof(TModule).Assembly
        || (
            _moduleStores.TryGetValue(typeof(TModule), out var store)
            && _moduleStores.Any(module => module.Value == store && module.Key.Assembly == commandType.Assembly)
        );

    public bool TryClaim(string connectionString) => _owners.TryAdd(StoreKey(connectionString), 0);

    public void Release(string connectionString) => _owners.TryRemove(StoreKey(connectionString), out _);

    private static string StoreKey(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{connection.Host?.ToLowerInvariant()}:{connection.Port}/{connection.Database ?? connection.Username}";
    }
}

internal sealed record PersistMessageModuleStore(Type ModuleType, string ConnectionString);
