using Booking.Booking.Features.CreatingBook.V1;
using MassTransit;

namespace Unit.Test.Fakes;

public static class FakeCreateBookingCommand
{
    public static CreateBooking Generate()
    {
        return new CreateBooking(
            PassengerId: NewId.NextGuid(),
            FlightId: NewId.NextGuid(),
            Description: "Test booking"
        )
        { Id = NewId.NextGuid() };
    }
}
