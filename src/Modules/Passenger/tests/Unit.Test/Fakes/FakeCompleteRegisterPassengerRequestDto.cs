using AutoBogus;

namespace Unit.Test.Fakes;

using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public sealed class FakeCompleteRegisterPassengerRequestDto : AutoFaker<CompleteRegisterPassengerRequestDto>
{
    public FakeCompleteRegisterPassengerRequestDto()
    {
        RuleFor(r => r.PassportNumber, _ => FakePassengerCreate.SeededPassportNumber);
        RuleFor(r => r.PassengerType, _ => PassengerType.Female);
        RuleFor(r => r.Age, _ => 25);
    }
}
