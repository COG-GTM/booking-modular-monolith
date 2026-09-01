using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

namespace Unit.Test.Fakes;

public static class FakeValidateCompleteRegisterPassengerCommand
{
    public static CompleteRegisterPassenger Generate()
    {
        return new CompleteRegisterPassenger(null!, (Passenger.Passengers.Enums.PassengerType)99, 0);
    }
}
