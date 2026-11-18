using AutoBogus;

namespace Integration.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class FakeRegisterNewUserCommand : AutoFaker<RegisterNewUser>
{
    public FakeRegisterNewUserCommand()
    {
        RuleFor(r => r.Username, x => "TestMyUser");
        RuleFor(r => r.Password, _ => "Flight#Deck2026");
        RuleFor(r => r.ConfirmPassword, _ => "Flight#Deck2026");
        RuleFor(r => r.Email, _ => "test@test.com");
    }
}