namespace Unit.Test.Fakes;

using global::Passenger.Passengers.ValueObjects;
using MassTransit;

public static class FakePassengerCreate
{
    public const string DefaultName = "Sam";
    public const string DefaultPassportNumber = "123456789";

    public static global::Passenger.Passengers.Models.Passenger Generate()
    {
        return global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(NewId.NextGuid()),
            Name.Of(DefaultName),
            PassportNumber.Of(DefaultPassportNumber)
        );
    }
}
