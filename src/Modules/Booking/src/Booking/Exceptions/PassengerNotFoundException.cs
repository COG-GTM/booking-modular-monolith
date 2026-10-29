using BuildingBlocks.Exception;

namespace Booking.Booking.Exceptions;

public class PassengerNotFoundException : NotFoundException
{
    public PassengerNotFoundException() : base("Passenger not found!")
    {
    }
}
