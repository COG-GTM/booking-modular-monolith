using System.Security.Claims;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class WebEventDispatchingExtensionsTests
{
    [Fact]
    public void RegistersDispatcherHttpContextAccessorAndHttpHeadersProvider()
    {
        var services = new ServiceCollection();

        services.AddWebEventDispatcher();

        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, d => d.ServiceType == typeof(IEventDispatcher)).Lifetime
        );
        var headers = Assert.Single(services, d => d.ServiceType == typeof(IEventHeadersProvider));
        Assert.Equal(typeof(HttpEventHeadersProvider), headers.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, headers.Lifetime);
        Assert.Contains(services, d => d.ServiceType == typeof(IHttpContextAccessor));
    }

    [Fact]
    public void ReplacesTheEmptyHeadersProviderWhenCoreDispatcherWasRegisteredFirst()
    {
        var services = new ServiceCollection();

        services.AddEventDispatcher();
        services.AddWebEventDispatcher();

        var headers = Assert.Single(services, d => d.ServiceType == typeof(IEventHeadersProvider));
        Assert.Equal(typeof(HttpEventHeadersProvider), headers.ImplementationType);
        Assert.Single(services, d => d.ServiceType == typeof(IEventDispatcher));
    }

    [Fact]
    public void KeepsTheHttpHeadersProviderWhenCoreDispatcherIsRegisteredAfterwards()
    {
        var services = new ServiceCollection();

        services.AddWebEventDispatcher();
        services.AddEventDispatcher();
        services.AddWebEventDispatcher();

        var headers = Assert.Single(services, d => d.ServiceType == typeof(IEventHeadersProvider));
        Assert.Equal(typeof(HttpEventHeadersProvider), headers.ImplementationType);
        Assert.Single(services, d => d.ServiceType == typeof(IEventDispatcher));
    }

    [Fact]
    public async Task DispatchedEnvelopesCarryHeadersFromTheCurrentRequest()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var services = new ServiceCollection();
        services.AddSingleton(publisher);
        services.AddWebEventDispatcher();
        services.AddEventMapper<SeatMapper>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-7")], "test")),
        };
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync<IDomainEvent>(new SeatReserved());

        await publisher
            .Received(1)
            .PublishAsync(
                Arg.Is<MessageEnvelope>(e =>
                    e.Message is SeatReservedIntegration && (string)e.Headers["UserId"]! == "user-7"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
