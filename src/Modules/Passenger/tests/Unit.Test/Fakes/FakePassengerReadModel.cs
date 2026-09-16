namespace Unit.Test.Fakes;

using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Models;
using MassTransit;

public static class FakePassengerReadModel
{
    public static PassengerReadModel Generate(Guid? passengerId = null, bool isDeleted = false)
    {
        return new PassengerReadModel
        {
            Id = NewId.NextGuid(),
            PassengerId = passengerId ?? NewId.NextGuid(),
            Name = "Sam",
            PassportNumber = "123456789",
            PassengerType = PassengerType.Male,
            Age = 30,
            IsDeleted = isDeleted,
        };
    }
}
