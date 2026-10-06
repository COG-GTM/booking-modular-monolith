using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.MassTransit;

// Handle inbox messages with masstransit pipeline; the inbox record is written to the store owned by
// the consumer's module (resolved through the consumer's assembly).
public class ConsumeFilter<TConsumer, TMessage> : IFilter<ConsumerConsumeContext<TConsumer, TMessage>>
    where TConsumer : class
    where TMessage : class
{
    private readonly IPersistMessageProcessor _persistMessageProcessor;

    public ConsumeFilter(IServiceProvider serviceProvider)
    {
        _persistMessageProcessor = serviceProvider.GetPersistMessageProcessor(typeof(TConsumer).Assembly);
    }

    public async Task Send(
        ConsumerConsumeContext<TConsumer, TMessage> context,
        IPipe<ConsumerConsumeContext<TConsumer, TMessage>> next
    )
    {
        var id = await _persistMessageProcessor.AddReceivedMessageAsync(
            new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value))
        );

        var message = await _persistMessageProcessor.ExistMessageAsync(id);

        if (message is null)
        {
            await next.Send(context);
            await _persistMessageProcessor.ProcessInboxAsync(id);
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("persistMessageInbox");
    }
}

// Maps each message type to the consumer type that handles it, built from the
// assemblies scanned by AddCustomMassTransit.
public class ConsumerTypeMap(IReadOnlyDictionary<Type, Type> consumerTypeByMessageType)
{
    public Type? ResolveConsumerType(Type messageType) =>
        consumerTypeByMessageType.GetValueOrDefault(messageType);

    public static ConsumerTypeMap FromAssemblies(IEnumerable<System.Reflection.Assembly> assemblies)
    {
        var map = new Dictionary<Type, Type>();

        foreach (var consumerType in assemblies
                     .SelectMany(assembly => assembly.GetTypes())
                     .Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var messageType in consumerType
                         .GetInterfaces()
                         .Where(i =>
                             i.IsGenericType &&
                             i.GetGenericTypeDefinition() == typeof(IConsumer<>))
                         .Select(i => i.GetGenericArguments()[0]))
            {
                map.TryAdd(messageType, consumerType);
            }
        }

        return new ConsumerTypeMap(map);
    }
}

// Scoped-filter wrapper registered through UseConsumeFilter (which only supports
// IFilter<ConsumeContext<T>>). Resolves the consumer's module via ConsumerTypeMap
// and writes the inbox record to that module's store.
public class ConsumeFilter<T>(
    IServiceProvider serviceProvider,
    ConsumerTypeMap consumerTypeMap
) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var consumerType = consumerTypeMap.ResolveConsumerType(typeof(T))
            ?? throw new InvalidOperationException(
                $"Unable to resolve the consuming type for message '{typeof(T).Name}'.");

        var persistMessageProcessor = serviceProvider.GetPersistMessageProcessor(consumerType.Assembly);

        var id = await persistMessageProcessor.AddReceivedMessageAsync(
            new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value))
        );

        var message = await persistMessageProcessor.ExistMessageAsync(id);

        if (message is null)
        {
            await next.Send(context);
            await persistMessageProcessor.ProcessInboxAsync(id);
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("persistMessageInbox");
    }
}
