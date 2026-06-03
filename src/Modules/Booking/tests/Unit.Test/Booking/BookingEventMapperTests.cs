using Booking;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Booking;

[Collection(nameof(Common.UnitTestFixture))]
public class BookingEventMapperTests
{
    private readonly BookingEventMapper _mapper;

    public BookingEventMapperTests()
    {
        _mapper = new BookingEventMapper();
    }

    [Fact]
    public void maps_booking_created_domain_event_to_booking_created_integration_event()
    {
        var id = Guid.NewGuid();
        var passengerInfo = PassengerInfo.Of("Test User");
        var trip = Trip.Of(
            "FL300",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            150m,
            "desc",
            "3C"
        );

        var domainEvent = new BookingCreatedDomainEvent(id, passengerInfo, trip) { Id = id };

        var integrationEvent = _mapper.MapToIntegrationEvent(domainEvent);

        integrationEvent.Should().NotBeNull();
        integrationEvent.Should().BeOfType<BookingCreated>();
        ((BookingCreated)integrationEvent!).Id.Should().Be(id);
    }

    [Fact]
    public void unknown_event_returns_null()
    {
        var unknownEvent = new FakeUnknownDomainEvent();

        var integrationEvent = _mapper.MapToIntegrationEvent(unknownEvent);

        integrationEvent.Should().BeNull();
    }

    [Fact]
    public void map_to_internal_command_always_returns_null()
    {
        var id = Guid.NewGuid();
        var passengerInfo = PassengerInfo.Of("Test User");
        var trip = Trip.Of(
            "FL300",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            150m,
            "desc",
            "3C"
        );

        var domainEvent = new BookingCreatedDomainEvent(id, passengerInfo, trip) { Id = id };

        var internalCommand = _mapper.MapToInternalCommand(domainEvent);

        internalCommand.Should().BeNull();
    }

    private class FakeUnknownDomainEvent : IDomainEvent { }
}
