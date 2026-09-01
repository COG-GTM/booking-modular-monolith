using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

namespace Unit.Test.Fakes;

public static class FakeCompleteRegisterPassengerCommand
{
    public static CompleteRegisterPassenger Generate()
    {
        return new CompleteRegisterPassenger("AB123456", PassengerType.Male, 30);
    }
}
