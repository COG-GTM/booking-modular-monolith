namespace Unit.Test.Fakes;

using global::Passenger.Passengers.ValueObjects;
using MassTransit;

public static class FakePassengerCreate
{
    public static readonly Guid SeededPassengerId = new("5c5c0000-97c6-fc34-2eb9-08db322230d1");
    public const string SeededPassportNumber = "123456789";

    public static global::Passenger.Passengers.Models.Passenger Generate(
        Guid? id = null,
        string passportNumber = "987654321"
    )
    {
        return global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(id ?? NewId.NextGuid()),
            Name.Of("Sam"),
            PassportNumber.Of(passportNumber)
        );
    }
}
