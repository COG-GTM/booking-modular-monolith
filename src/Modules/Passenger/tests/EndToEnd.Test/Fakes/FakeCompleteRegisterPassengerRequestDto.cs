namespace EndToEnd.Test.Fakes;

using AutoBogus;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public sealed class FakeCompleteRegisterPassengerRequestDto : AutoFaker<CompleteRegisterPassengerRequestDto>
{
    public FakeCompleteRegisterPassengerRequestDto(string passportNumber)
    {
        RuleFor(r => r.PassportNumber, _ => passportNumber);
        RuleFor(r => r.PassengerType, _ => PassengerType.Male);
        RuleFor(r => r.Age, _ => 30);
    }
}
