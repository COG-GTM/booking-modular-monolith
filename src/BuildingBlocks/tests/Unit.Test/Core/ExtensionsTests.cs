using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Core;

public class ExtensionsTests
{
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
