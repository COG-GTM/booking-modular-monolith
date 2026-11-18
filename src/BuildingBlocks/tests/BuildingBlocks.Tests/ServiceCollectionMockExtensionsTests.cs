using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class ServiceCollectionMockExtensionsTests
{
    [Fact]
    public async Task replaces_a_scoped_service_with_one_shared_singleton_mock()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMockableService, RealMockableService>();
        services.ReplaceServiceWithSingletonMock<IMockableService>();
        services
            .Where(descriptor => descriptor.ServiceType == typeof(IMockableService))
            .Should()
            .ContainSingle()
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        await using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IMockableService>();
        var second = secondScope.ServiceProvider.GetRequiredService<IMockableService>();
        first.Should().NotBeOfType<RealMockableService>();
        first.Should().BeSameAs(second);
        first.Invoke();

        first.Received(1).Invoke();
    }

    [Fact]
    public async Task adds_a_singleton_mock_when_the_service_is_not_registered()
    {
        var services = new ServiceCollection();

        services.ReplaceServiceWithSingletonMock<IMockableService>();

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IMockableService))
            .Should()
            .ContainSingle()
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMockableService>().Should().NotBeNull();
    }

    public interface IMockableService
    {
        void Invoke();
    }

    private sealed class RealMockableService : IMockableService
    {
        public void Invoke() { }
    }
}
