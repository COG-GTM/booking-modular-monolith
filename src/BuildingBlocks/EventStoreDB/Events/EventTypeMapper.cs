using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.EventStoreDB.Events;

// Event type names come from the store and must only resolve to event types registered by the application,
// never to arbitrary types loaded in the process.
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

    public static void RegisterEventTypes(params Assembly[] assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(GetLoadableTypes).Where(IsEventType))
            ToName(type);
    }

    public static string ToName<TEventType>() => ToName(typeof(TEventType));

    public static string ToName(Type eventType) => Instance.typeNameMap.GetOrAdd(eventType, _ =>
    {
        var eventTypeName = eventType.FullName!.Replace(".", "_", StringComparison.Ordinal);

        Instance.typeMap.AddOrUpdate(eventTypeName, eventType, (_, _) => eventType);

        return eventTypeName;
    });

    public static Type? ToType(string eventTypeName) =>
        Instance.typeMap.TryGetValue(eventTypeName, out var type) ? type : null;

    private static bool IsEventType(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false, FullName: not null } &&
        typeof(IEvent).IsAssignableFrom(type) &&
        !typeof(StreamEvent).IsAssignableFrom(type);

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null)!;
        }
    }
}
