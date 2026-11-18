using FluentAssertions;
using global::Booking.Booking.Features.CreatingBook.V1;
using global::Booking.Booking.ValueObjects;
using Xunit;
using BookingModel = global::Booking.Booking.Models.Booking;

namespace Integration.Test.Booking;

public class BookingAuditTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void create_should_attribute_domain_event_to_supplied_user(bool anonymous)
    {
        Guid? userId = anonymous ? null : Guid.NewGuid();
        var passenger = PassengerInfo.Of("Alice");
        var trip = Trip.Of(
            "AB123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            100m,
            "Outbound",
            "1A"
        );

        var booking = BookingModel.Create(Guid.NewGuid(), passenger, trip, userId: userId);

        var created = booking
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Subject;
        created.CreatedBy.Should().Be(userId);
        created.Id.Should().Be(booking.Id);
    }
}
