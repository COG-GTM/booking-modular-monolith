using System.Linq;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test;

using global::Booking;
using global::Booking.Booking.Features.CreatingBook.V1;

[Collection(nameof(UnitTestFixture))]
public class BookingEventMapperTests
{
    private readonly BookingEventMapper _mapper = new();

    [Fact]
    public void booking_created_domain_event_should_map_to_booking_created_integration_event()
    {
        // Arrange
        var booking = FakeBookingCreate.Generate();
        var domainEvent = (BookingCreatedDomainEvent)booking.DomainEvents.Single();

        // Act
        var integrationEvent = _mapper.MapToIntegrationEvent(domainEvent);

        // Assert
        integrationEvent.Should().BeOfType<BookingCreated>();
        ((BookingCreated)integrationEvent).Id.Should().Be(booking.Id);
    }

    [Fact]
    public void booking_created_domain_event_should_not_map_to_internal_command()
    {
        // Arrange
        var booking = FakeBookingCreate.Generate();
        var domainEvent = (BookingCreatedDomainEvent)booking.DomainEvents.Single();

        // Act
        var command = _mapper.MapToInternalCommand(domainEvent);

        // Assert
        command.Should().BeNull();
    }
}
