using BuildingBlocks.Exception;

namespace Flight.Seats.Exceptions;

public class SeatAlreadyReservedException : ConflictException
{
    public SeatAlreadyReservedException(int? code = default) : base("Seat is already reserved!", code)
    {
    }
}
