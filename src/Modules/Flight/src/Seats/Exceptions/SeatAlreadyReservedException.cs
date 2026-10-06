using System.Net;
using BuildingBlocks.Exception;

namespace Flight.Seats.Exceptions;

public class SeatAlreadyReservedException : AppException
{
    public SeatAlreadyReservedException(int? code = default) : base("Seat is already reserved!", HttpStatusCode.Conflict, code)
    {
    }
}
