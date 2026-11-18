using System.Net;
using BuildingBlocks.Exception;

namespace Passenger.Passengers.Exceptions;

public class PassportNumberAlreadyLinkedException : AppException
{
    public PassportNumberAlreadyLinkedException()
        : base("Passport number is already linked to another user.", HttpStatusCode.Conflict) { }
}
