using AutoBogus;

namespace Unit.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public sealed class FakeRegisterNewUserRequestDto : AutoFaker<RegisterNewUserRequestDto>
{
    public FakeRegisterNewUserRequestDto()
    {
        RuleFor(r => r.FirstName, _ => "Test");
        RuleFor(r => r.LastName, _ => "User");
        RuleFor(r => r.Username, _ => "TestMyUser");
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Password@123");
        RuleFor(r => r.Email, _ => "test@test.com");
        RuleFor(r => r.PassportNumber, _ => "1234567890");
    }
}
