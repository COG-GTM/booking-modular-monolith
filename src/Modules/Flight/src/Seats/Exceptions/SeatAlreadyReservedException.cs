using BuildingBlocks.Exception;

namespace Flight.Seats.Exceptions;

public class SeatAlreadyReservedException : ConflictException
{
    public SeatAlreadyReservedException() : base("Seat is already reserved!")
    {
    }
}
