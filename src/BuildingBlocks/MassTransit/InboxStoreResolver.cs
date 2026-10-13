using System.Reflection;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;

namespace BuildingBlocks.MassTransit;

// Maps a receive endpoint (queue) to the outbox/inbox store of the module that owns its consumer,
// so inbox rows land in the consuming module's IPersistMessageProcessor<TModule> store.
public sealed class InboxStoreResolver
{
    private static readonly MethodInfo ConsumerNameMethod = typeof(IEndpointNameFormatter).GetMethod(
        nameof(IEndpointNameFormatter.Consumer)
    )!;

    private readonly Dictionary<string, Type> _processorTypeByEndpoint;

    public InboxStoreResolver(
        IEnumerable<Assembly> consumerAssemblies,
        IEndpointNameFormatter endpointNameFormatter,
        IEnumerable<PersistMessageStore> stores
    )
    {
        var processorTypeByAssembly = stores
            .Where(store => store.DbContextType.IsGenericType)
            .Select(store => store.DbContextType.GetGenericArguments()[0])
            .GroupBy(module => module.Assembly)
            .ToDictionary(g => g.Key, g => typeof(IPersistMessageProcessor<>).MakeGenericType(g.First()));

        _processorTypeByEndpoint = consumerAssemblies
            .Where(processorTypeByAssembly.ContainsKey)
            .SelectMany(assembly => GetConsumerTypes(assembly).Select(consumer => (assembly, consumer)))
            .Select(x =>
                (
                    Endpoint: (string)
                        ConsumerNameMethod.MakeGenericMethod(x.consumer).Invoke(endpointNameFormatter, null)!,
                    ProcessorType: processorTypeByAssembly[x.assembly]
                )
            )
            .GroupBy(x => x.Endpoint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ProcessorType, StringComparer.OrdinalIgnoreCase);
    }

    public IPersistMessageProcessor? Resolve(IServiceProvider serviceProvider, Uri? inputAddress)
    {
        var endpoint = inputAddress?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        if (endpoint is not null && _processorTypeByEndpoint.TryGetValue(endpoint, out var processorType))
        {
            return serviceProvider.GetService(processorType) as IPersistMessageProcessor;
        }

        return serviceProvider.GetService(typeof(IPersistMessageProcessor)) as IPersistMessageProcessor;
    }

    private static IEnumerable<Type> GetConsumerTypes(Assembly assembly) =>
        assembly
            .GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && typeof(IConsumer).IsAssignableFrom(t)
            );
}
