using AutoBogus;
using BuildingBlocks.Contracts.EventBus.Messages;

namespace Unit.Test.Fakes;

public sealed class FakeUserCreated : AutoFaker<UserCreated>
{
    public FakeUserCreated()
    {
        RuleFor(x => x.Name, _ => "Sam Passenger");
        RuleFor(x => x.PassportNumber, _ => "123456789");
    }
}
