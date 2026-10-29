using AutoBogus;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using MassTransit;

namespace Passenger.Host.Test.Fakes;

public sealed class FakeCompleteRegisterPassengerMongoCommand : AutoFaker<CompleteRegisterPassengerMongoCommand>
{
    public FakeCompleteRegisterPassengerMongoCommand()
    {
        RuleFor(command => command.Id, _ => NewId.NextGuid());
        RuleFor(command => command.Name, _ => "Sam");
        RuleFor(command => command.PassportNumber, _ => "123456789");
        RuleFor(command => command.Age, _ => 30);
        RuleFor(command => command.IsDeleted, _ => false);
        RuleFor(command => command.PassengerType, (_, _) => global::Passenger.Passengers.Enums.PassengerType.Male);
    }
}
