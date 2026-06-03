using Booking.Booking.Features.CreatingBook.V1;

namespace Unit.Test.Fakes;

public static class FakeValidateCreateBookingCommand
{
    public static CreateBooking Generate()
    {
        return new CreateBooking(
            PassengerId: Guid.Empty,
            FlightId: Guid.Empty,
            Description: ""
        );
    }
}
