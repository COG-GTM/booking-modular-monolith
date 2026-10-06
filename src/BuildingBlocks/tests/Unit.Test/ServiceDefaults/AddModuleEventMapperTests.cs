namespace Unit.Test.ServiceDefaults;

using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public class AddModuleEventMapperTests
{
    private class FakeEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }

    [Fact]
    public void should_resolve_the_registered_module_mapper_as_the_host_event_mapper()
    {
        var services = new ServiceCollection();
        services.AddScoped<FakeEventMapper>();
        services.AddModuleEventMapper<FakeEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        eventMapper.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<FakeEventMapper>());
    }

    [Fact]
    public void should_resolve_a_different_event_mapper_per_scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<FakeEventMapper>();
        services.AddModuleEventMapper<FakeEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        firstScope
            .ServiceProvider.GetRequiredService<IEventMapper>()
            .Should()
            .NotBeSameAs(secondScope.ServiceProvider.GetRequiredService<IEventMapper>());
    }

    [Fact]
    public void should_throw_when_the_module_mapper_itself_is_not_registered()
    {
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FakeEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IEventMapper>();

        act.Should().Throw<InvalidOperationException>();
    }
}
