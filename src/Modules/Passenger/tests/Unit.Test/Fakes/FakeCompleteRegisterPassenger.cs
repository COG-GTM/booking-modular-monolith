using AutoBogus;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

namespace Unit.Test.Fakes;

public sealed class FakeCompleteRegisterPassenger : AutoFaker<CompleteRegisterPassenger>
{
    public FakeCompleteRegisterPassenger()
    {
        RuleFor(x => x.PassportNumber, _ => "123456789");
        RuleFor(x => x.PassengerType, _ => PassengerType.Male);
        RuleFor(x => x.Age, _ => 30);
    }
}
