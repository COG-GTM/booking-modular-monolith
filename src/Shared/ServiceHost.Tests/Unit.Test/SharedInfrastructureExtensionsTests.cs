using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shared.ServiceHost;
using Xunit;

namespace ServiceHost.Unit.Test;

public class SharedInfrastructureExtensionsTests
{
    [Fact]
    public void add_event_mapper_should_resolve_ieventmapper_as_registered_mapper()
    {
        var services = new ServiceCollection();
        services.AddScoped<StubEventMapper>();

        services.AddEventMapper<StubEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        mapper.Should().BeOfType<StubEventMapper>();
    }

    [Fact]
    public void add_event_mapper_should_register_scoped_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<StubEventMapper>();

        services.AddEventMapper<StubEventMapper>();

        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<IEventMapper>();
        var second = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        using var otherScope = provider.CreateScope();
        var third = otherScope.ServiceProvider.GetRequiredService<IEventMapper>();

        first.Should().BeSameAs(second);
        third.Should().NotBeSameAs(first);
    }

    private sealed class StubEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }
}
