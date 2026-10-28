using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class TestServiceCollectionExtensionsTests
{
    [Fact]
    public void ReplacesTheRegisteredServiceWithASingletonSubstitute()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEventPublisher, ThrowingPublisher>();

        var returned = services.ReplaceServiceWithSingletonMock<IEventPublisher>();

        Assert.Same(services, returned);
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEventPublisher));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IEventPublisher>();
        Assert.IsNotType<ThrowingPublisher>(first);
        Assert.Same(first, provider.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public void AddsASubstituteWhenTheServiceWasNotRegistered()
    {
        var services = new ServiceCollection();

        services.ReplaceServiceWithSingletonMock<IEventHeadersProvider>();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IEventHeadersProvider>());
    }

    private sealed class ThrowingPublisher : IEventPublisher
    {
        public Task PublishAsync(
            BuildingBlocks.Core.Event.MessageEnvelope message,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SendInternalAsync(
            BuildingBlocks.Core.Event.IInternalCommand command,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
