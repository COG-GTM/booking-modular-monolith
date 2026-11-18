using System.Collections.Concurrent;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.EventStoreDB.Events;

public class EventTypeMapper
{
    private static readonly EventTypeMapper Instance = new();

    private readonly ConcurrentDictionary<string, Type?> typeMap = new();
    private readonly ConcurrentDictionary<Type, string> typeNameMap = new();

    public static void AddCustomMap<T>(string mappedEventTypeName) => AddCustomMap(typeof(T), mappedEventTypeName);

    public static void AddCustomMap(Type eventType, string mappedEventTypeName)
    {
        Instance.typeNameMap.AddOrUpdate(eventType, mappedEventTypeName, (_, _) => mappedEventTypeName);
        Instance.typeMap.AddOrUpdate(mappedEventTypeName, eventType, (_, _) => eventType);
    }

    public static string ToName<TEventType>() => ToName(typeof(TEventType));

    public static string ToName(Type eventType) => Instance.typeNameMap.GetOrAdd(eventType, _ =>
    {
        var eventTypeName = eventType.FullName!.Replace(".", "_", StringComparison.CurrentCulture);

        Instance.typeMap.AddOrUpdate(eventTypeName, eventType, (_, _) => eventType);

        return eventTypeName;
    });

    public static Type? ToType(string eventTypeName)
    {
        if (Instance.typeMap.TryGetValue(eventTypeName, out var mappedType))
            return mappedType;

        var type = FindEventType(eventTypeName.Replace("_", ".", StringComparison.CurrentCulture));

        if (type == null)
            return null;

        Instance.typeMap.TryAdd(eventTypeName, type);
        Instance.typeNameMap.TryAdd(type, eventTypeName);

        return type;
    }

    // Event type names come from the event store, so only concrete IEvent types are resolved.
    private static Type? FindEventType(string fullTypeName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(x => x.FullName == fullTypeName && IsAllowedEventType(x));

    public static bool IsAllowedEventType(Type type) =>
        type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
        typeof(IEvent).IsAssignableFrom(type);
}
