using MassTransit;
using Passenger.Passengers.ValueObjects;

namespace Unit.Test.Fakes;

public static class FakePassengerCreate
{
    public static global::Passenger.Passengers.Models.Passenger Generate()
    {
        return global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(NewId.NextGuid()),
            Name.Of("Jane Doe"),
            PassportNumber.Of("CD789012")
        );
    }
}
