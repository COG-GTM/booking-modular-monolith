using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class PassengerAccessDeniedException : ForbiddenException
{
    public PassengerAccessDeniedException() : base("You are not allowed to book for this passenger!")
    {
    }
}
