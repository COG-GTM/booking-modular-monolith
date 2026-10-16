using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class ServiceCollectionExtensionsTests
{
    public interface IWebSwappable;

    private sealed class WebOriginal : IWebSwappable;

    private sealed class WebReplacement : IWebSwappable;

    [Fact]
    public void replace_scoped_leaves_one_descriptor_with_the_new_implementation_and_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWebSwappable, WebOriginal>();

        services.ReplaceScoped<IWebSwappable, WebReplacement>();

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IWebSwappable)).Which;
        descriptor.ImplementationType.Should().Be(typeof(WebReplacement));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void replace_scoped_factory_overload_uses_the_factory_and_scoped_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWebSwappable, WebOriginal>();

        services.ReplaceScoped<IWebSwappable>(_ => new WebReplacement());

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IWebSwappable)).Which;
        descriptor.ImplementationFactory.Should().NotBeNull();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void replace_transient_and_singleton_apply_their_lifetimes()
    {
        var services = new ServiceCollection();
        services.AddTransient<IWebSwappable, WebOriginal>();
        services.ReplaceTransient<IWebSwappable, WebReplacement>();
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IWebSwappable))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Transient);

        services.ReplaceSingleton<IWebSwappable>(_ => new WebOriginal());
        services
            .Should()
            .ContainSingle(d => d.ServiceType == typeof(IWebSwappable))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);

        services.ReplaceSingleton<IWebSwappable, WebReplacement>();
        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IWebSwappable)).Which;
        descriptor.ImplementationType.Should().Be(typeof(WebReplacement));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void replace_transient_factory_overload_uses_the_factory()
    {
        var services = new ServiceCollection();
        services.AddTransient<IWebSwappable, WebOriginal>();

        services.ReplaceTransient<IWebSwappable>(_ => new WebReplacement());

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IWebSwappable)).Which;
        descriptor.ImplementationFactory.Should().NotBeNull();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void unregister_removes_the_descriptor_and_is_a_no_op_when_absent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebSwappable, WebOriginal>();

        services.Unregister<IWebSwappable>();
        services.Unregister<IWebSwappable>();

        services.Should().NotContain(d => d.ServiceType == typeof(IWebSwappable));
    }

    [Fact]
    public void replace_service_with_singleton_mock_registers_an_nsubstitute_proxy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebSwappable, WebOriginal>();

        services.ReplaceServiceWithSingletonMock<IWebSwappable>();

        using var provider = services.BuildServiceProvider();
        var mock = provider.GetRequiredService<IWebSwappable>();
        mock.Should().BeAssignableTo<IWebSwappable>();
        mock.Should().NotBeOfType<WebOriginal>();
        provider.GetRequiredService<IWebSwappable>().Should().BeSameAs(mock);
    }
}
