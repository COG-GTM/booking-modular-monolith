using System.Reflection;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.MassTransit;

// Handle inbox messages with masstransit pipeline
public sealed record ConsumerModuleRegistration(string EndpointName, Assembly ConsumerAssembly);

public class ConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly IServiceProvider _services;
    private readonly IEnumerable<ConsumerModuleRegistration> _consumers;
    private readonly IEnumerable<ModulePersistenceRegistration> _modules;

    public ConsumeFilter(
        IServiceProvider services,
        IEnumerable<ConsumerModuleRegistration> consumers,
        IEnumerable<ModulePersistenceRegistration> modules
    )
    {
        _services = services;
        _consumers = consumers;
        _modules = modules;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var messageId =
            context.MessageId
            ?? throw new InvalidOperationException("An inbox message must have a MassTransit MessageId.");
        var endpoint = Uri.UnescapeDataString(context.ReceiveContext.InputAddress.Segments.Last());
        var assembly = _consumers.Single(x => x.EndpointName == endpoint).ConsumerAssembly;
        var module = _modules.Single(x => x.ModuleType.Assembly == assembly);
        await module
            .ResolveProcessor(_services)
            .ExecuteInboxAsync(
                messageId,
                context.ReceiveContext.InputAddress.AbsolutePath,
                () => next.Send(context),
                context.CancellationToken
            );
    }

    public void Probe(ProbeContext context) { }
}
