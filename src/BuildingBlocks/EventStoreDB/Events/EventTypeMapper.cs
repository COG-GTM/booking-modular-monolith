using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.EventStoreDB.Events;

public class EventTypeMapper
{
    private static readonly EventTypeMapper Instance = new();

    private readonly ConcurrentDictionary<string, Type> typeMap = new();
    private readonly ConcurrentDictionary<Type, string> typeNameMap = new();

    public static void AddCustomMap<T>(string mappedEventTypeName) => AddCustomMap(typeof(T), mappedEventTypeName);

    public static void AddCustomMap(Type eventType, string mappedEventTypeName)
    {
        Instance.typeNameMap.AddOrUpdate(eventType, mappedEventTypeName, (_, _) => mappedEventTypeName);
        Instance.typeMap.AddOrUpdate(mappedEventTypeName, eventType, (_, _) => eventType);
    }

    public static void AddDomainEventTypes(params Assembly[] assemblies)
    {
        var eventTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } &&
                        typeof(IDomainEvent).IsAssignableFrom(t));

        foreach (var eventType in eventTypes)
            ToName(eventType);
    }

    public static string ToName<TEventType>() => ToName(typeof(TEventType));

    public static string ToName(Type eventType) => Instance.typeNameMap.GetOrAdd(eventType, _ =>
    {
        var eventTypeName = eventType.FullName!.Replace(".", "_", StringComparison.CurrentCulture);

        Instance.typeMap.AddOrUpdate(eventTypeName, eventType, (_, _) => eventType);

        return eventTypeName;
    });

    // Only names registered through AddCustomMap, AddDomainEventTypes or ToName resolve;
    // event type names come from the event store and must never select arbitrary loaded types.
    public static Type? ToType(string eventTypeName) =>
        Instance.typeMap.TryGetValue(eventTypeName, out var type) ? type : null;
}
