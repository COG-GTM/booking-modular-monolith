using System.Net;
using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class SeatNotAvailableException : AppException
{
    public SeatNotAvailableException(int? code = default) : base("No available seat for this flight!", HttpStatusCode.Conflict, code)
    {
    }
}
