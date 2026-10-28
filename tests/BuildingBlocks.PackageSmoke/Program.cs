using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var publisher = new Publisher();
services.AddSingleton<IEventPublisher>(publisher);
services.AddEventDispatcher();
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync<IIntegrationEvent>(new SmokeEvent());
if (publisher.Count != 1)
    throw new InvalidOperationException("Package consumer did not dispatch its event.");
Console.WriteLine("Local NuGet consumer dispatched an integration event without EF Core.");

internal record SmokeEvent : IIntegrationEvent;

internal sealed class Publisher : IEventPublisher
{
    public int Count { get; private set; }

    public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        Count++;
        return Task.CompletedTask;
    }

    public Task SendInternalAsync(IInternalCommand command, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
