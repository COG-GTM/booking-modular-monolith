using BuildingBlocks.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Unit.Test.Core.Fakes;
using Xunit;

namespace Unit.Test.Core;

public class EventMapperExtensionsTests
{
    [Fact]
    public void add_module_event_mapper_should_register_mapper_and_registration_as_scoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddModuleEventMapper<FakeMatchingEventMapper>();

        // Assert
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(FakeMatchingEventMapper) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IEventMapperRegistration) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void add_module_event_mapper_registration_should_expose_same_scoped_mapper_instance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FakeMatchingEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var mapper = scope.ServiceProvider.GetRequiredService<FakeMatchingEventMapper>();
        var registration = scope.ServiceProvider.GetRequiredService<IEventMapperRegistration>();

        // Assert
        registration.Mapper.Should().BeSameAs(mapper);
    }

    [Fact]
    public void add_composite_event_mapper_should_resolve_composite_event_mapper()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCompositeEventMapper();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        // Assert
        eventMapper.Should().BeOfType<CompositeEventMapper>();
    }

    [Fact]
    public void composite_event_mapper_should_delegate_to_registered_module_mappers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FakeNonMatchingEventMapper>();
        services.AddModuleEventMapper<FakeMatchingEventMapper>();
        services.AddCompositeEventMapper();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        // Act
        var integrationEvent = eventMapper.MapToIntegrationEvent(new FakeDomainEvent());
        var internalCommand = eventMapper.MapToInternalCommand(new FakeDomainEvent());

        // Assert
        integrationEvent.Should().BeOfType<FakeIntegrationEvent>();
        internalCommand.Should().BeOfType<FakeInternalCommand>();
    }

    [Fact]
    public void composite_event_mapper_without_registrations_should_return_null()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCompositeEventMapper();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        // Act
        var integrationEvent = eventMapper.MapToIntegrationEvent(new FakeDomainEvent());
        var internalCommand = eventMapper.MapToInternalCommand(new FakeDomainEvent());

        // Assert
        integrationEvent.Should().BeNull();
        internalCommand.Should().BeNull();
    }

    [Fact]
    public void composite_event_mapper_should_return_null_when_no_mapper_matches()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FakeNonMatchingEventMapper>();
        services.AddCompositeEventMapper();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        // Act
        var integrationEvent = eventMapper.MapToIntegrationEvent(new FakeDomainEvent());
        var internalCommand = eventMapper.MapToInternalCommand(new FakeDomainEvent());

        // Assert
        integrationEvent.Should().BeNull();
        internalCommand.Should().BeNull();
    }

    [Fact]
    public void add_module_event_mapper_for_multiple_mappers_should_register_all_registrations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FakeMatchingEventMapper>();
        services.AddModuleEventMapper<FakeNonMatchingEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var registrations = scope.ServiceProvider.GetServices<IEventMapperRegistration>().ToList();

        // Assert
        registrations.Should().HaveCount(2);
        registrations.Select(registration => registration.Mapper.GetType())
            .Should()
            .BeEquivalentTo(new[] { typeof(FakeMatchingEventMapper), typeof(FakeNonMatchingEventMapper) });
    }
}
