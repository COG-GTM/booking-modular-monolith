namespace Unit.Test.Fakes;

using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Models;
using global::Passenger.Passengers.ValueObjects;

public static class FakePassengerCompleteRegistration
{
    public const PassengerType DefaultPassengerType = PassengerType.Male;
    public const int DefaultAge = 30;

    public static void Generate(Passenger passenger)
    {
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            DefaultPassengerType,
            Age.Of(DefaultAge)
        );
    }
}
