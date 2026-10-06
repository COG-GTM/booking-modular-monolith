using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class SeatNotAvailableException : ConflictException
{
    public SeatNotAvailableException(int? code = default) : base("No available seat for this flight!", code)
    {
    }
}
