using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class PassengerBookingForbiddenException : ForbiddenException
{
    public PassengerBookingForbiddenException()
        : base("You are not allowed to create a booking for this passenger.") { }
}
