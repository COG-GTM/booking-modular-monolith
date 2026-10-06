using System.Net;
using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class PassengerAccessForbiddenException : AppException
{
    public PassengerAccessForbiddenException(int? code = default)
        : base("Passenger does not belong to the current user!", HttpStatusCode.Forbidden, code) { }
}
