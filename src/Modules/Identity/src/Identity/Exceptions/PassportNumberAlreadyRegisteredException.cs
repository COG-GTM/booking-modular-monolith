using System.Net;
using BuildingBlocks.Exception;

namespace Identity.Identity.Exceptions;

public class PassportNumberAlreadyRegisteredException : AppException
{
    public PassportNumberAlreadyRegisteredException()
        : base("An account is already registered with this passport number.", HttpStatusCode.Conflict) { }
}
