using System.Net;
using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class NoAvailableSeatException : AppException
{
    public NoAvailableSeatException()
        : base("No available seat for this flight!", HttpStatusCode.Conflict) { }
}
