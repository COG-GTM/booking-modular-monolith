using AutoBogus;

namespace Integration.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class FakeValidateRegisterNewUserCommand : AutoFaker<RegisterNewUser>
{
    public FakeValidateRegisterNewUserCommand()
    {
        RuleFor(r => r.FirstName, _ => string.Empty);
        RuleFor(r => r.LastName, _ => string.Empty);
        RuleFor(r => r.Username, _ => string.Empty);
        RuleFor(r => r.Email, _ => "not-an-email");
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Different@123");
    }
}
