using Booking;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shared.ServiceHost;
using Xunit;

namespace BookingService.Unit.Test;

public class BookingServiceEventMapperTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<BookingEventMapper>();

        services.AddEventMapper<BookingEventMapper>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void add_event_mapper_should_resolve_booking_event_mapper_as_ieventmapper()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        mapper.Should().BeOfType<BookingEventMapper>();
    }

    [Fact]
    public void booking_created_domain_event_should_map_to_integration_event()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        var bookingId = Guid.NewGuid();
        var domainEvent = new BookingCreatedDomainEvent(
            bookingId,
            PassengerInfo.Of("John Doe"),
            Trip.Of(
                "BA123",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow.AddDays(1),
                120m,
                "London to Paris",
                "12A"
            )
        )
        {
            Id = bookingId,
        };

        var integrationEvent = mapper.MapToIntegrationEvent(domainEvent);

        integrationEvent.Should().BeOfType<BookingCreated>().Which.Id.Should().Be(bookingId);
    }

    [Fact]
    public void unmapped_domain_event_should_return_null()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        var integrationEvent = mapper.MapToIntegrationEvent(new UnknownDomainEvent());
        var internalCommand = mapper.MapToInternalCommand(new UnknownDomainEvent());

        integrationEvent.Should().BeNull();
        internalCommand.Should().BeNull();
    }

    private sealed record UnknownDomainEvent : IDomainEvent;
}
