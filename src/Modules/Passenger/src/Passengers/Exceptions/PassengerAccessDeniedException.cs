namespace Passenger.Passengers.Exceptions;

using BuildingBlocks.Exception;

public class PassengerAccessDeniedException : ForbiddenException
{
    public PassengerAccessDeniedException(string code = default) : base("You are not allowed to modify this passenger!")
    {
    }
}
