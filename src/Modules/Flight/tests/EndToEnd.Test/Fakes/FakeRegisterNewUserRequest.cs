using AutoBogus;

namespace EndToEnd.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public sealed class FakeRegisterNewUserRequest : AutoFaker<RegisterNewUserRequestDto>
{
    public FakeRegisterNewUserRequest()
    {
        RuleFor(r => r.Username, f => f.Internet.UserName());
        RuleFor(r => r.Email, f => f.Internet.Email());
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Password@123");
        RuleFor(r => r.PassportNumber, f => f.Random.AlphaNumeric(9));
    }
}
