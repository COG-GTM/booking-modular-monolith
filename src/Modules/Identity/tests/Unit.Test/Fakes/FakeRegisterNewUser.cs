using AutoBogus;

namespace Unit.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class FakeRegisterNewUser : AutoFaker<RegisterNewUser>
{
    public FakeRegisterNewUser()
    {
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Password@123");
        RuleFor(r => r.Email, f => f.Internet.Email());
    }
}
