using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Core;

public class ExtensionsTests
{
    [Fact]
    public void add_event_dispatcher_should_register_dispatcher_and_default_headers_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<IIntegrationEventPublisher>());

        services.AddEventDispatcher();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventDispatcher>().Should().BeOfType<EventDispatcher>();
        scope.ServiceProvider.GetRequiredService<IEventHeadersProvider>()
            .Should().BeOfType<DefaultEventHeadersProvider>();
    }

    [Fact]
    public void add_event_dispatcher_should_not_override_existing_headers_provider()
    {
        var services = new ServiceCollection();
        var customProvider = Substitute.For<IEventHeadersProvider>();
        services.AddScoped(_ => customProvider);

        services.AddEventDispatcher();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventHeadersProvider>().Should().BeSameAs(customProvider);
    }

    [Fact]
    public void add_event_mapper_should_register_multiple_mappers()
    {
        var services = new ServiceCollection();

        services.AddEventMapper<FirstEventMapper>();
        services.AddEventMapper<SecondEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var mappers = scope.ServiceProvider.GetServices<IEventMapper>().ToList();
        mappers.Should().HaveCount(2);
        mappers.Should().ContainSingle(m => m is FirstEventMapper);
        mappers.Should().ContainSingle(m => m is SecondEventMapper);
    }

    private sealed class FirstEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;
        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }

    private sealed class SecondEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;
        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }
}
