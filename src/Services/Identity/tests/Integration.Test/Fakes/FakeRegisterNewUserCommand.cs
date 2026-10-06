using AutoBogus;
using RegisterNewUser = global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUser;

namespace Identity.Host.Integration.Test.Fakes;

public class FakeRegisterNewUserCommand : AutoFaker<RegisterNewUser>
{
    public FakeRegisterNewUserCommand()
    {
        RuleFor(r => r.Username, _ => "TestMyUser");
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Password@123");
        RuleFor(r => r.Email, _ => "test@test.com");
    }
}
