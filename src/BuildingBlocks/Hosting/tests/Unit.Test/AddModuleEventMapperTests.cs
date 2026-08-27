using BuildingBlocks.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shared.Hosting;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test;

public class AddModuleEventMapperTests
{
    private static ServiceProvider BuildProvider()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<FakeEventMapper>();

        builder.AddModuleEventMapper<FakeEventMapper>();

        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void add_module_event_mapper_should_register_scoped_composite_event_mapper()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        Assert.IsType<CompositeEventMapper>(eventMapper);
    }

    [Fact]
    public void resolved_mapper_should_delegate_map_to_integration_event_to_module_mapper()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        var integrationEvent = eventMapper.MapToIntegrationEvent(new FakeDomainEvent());

        Assert.IsType<FakeIntegrationEvent>(integrationEvent);
    }

    [Fact]
    public void resolved_mapper_should_delegate_map_to_internal_command_to_module_mapper()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        var internalCommand = eventMapper.MapToInternalCommand(new FakeDomainEvent());

        Assert.IsType<FakeInternalCommand>(internalCommand);
    }

    [Fact]
    public void resolved_mapper_should_return_null_for_events_the_module_mapper_does_not_map()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        Assert.Null(eventMapper.MapToIntegrationEvent(new FakeUnmappedDomainEvent()));
        Assert.Null(eventMapper.MapToInternalCommand(new FakeUnmappedDomainEvent()));
    }
}
