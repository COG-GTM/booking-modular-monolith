using AutoBogus;
using BuildingBlocks.Contracts.EventBus.Messages;

namespace Unit.Test.Fakes;

using MassTransit;

public sealed class FakeUserCreated : AutoFaker<UserCreated>
{
    public FakeUserCreated(string passportNumber = "555555555")
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.Name, _ => "Sam");
        RuleFor(r => r.PassportNumber, _ => passportNumber);
    }
}
