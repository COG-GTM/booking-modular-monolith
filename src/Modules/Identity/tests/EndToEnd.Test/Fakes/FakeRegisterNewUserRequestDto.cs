using AutoBogus;

namespace EndToEnd.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class FakeRegisterNewUserRequestDto : AutoFaker<RegisterNewUserRequestDto>
{
    public FakeRegisterNewUserRequestDto()
    {
        RuleFor(r => r.FirstName, _ => "Test");
        RuleFor(r => r.LastName, _ => "User");
        RuleFor(r => r.Username, _ => "e2e_" + Guid.NewGuid().ToString("N")[..8]);
        RuleFor(r => r.Email, _ => Guid.NewGuid().ToString("N")[..8] + "@test.com");
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Password@123");
        RuleFor(r => r.PassportNumber, _ => "P" + Guid.NewGuid().ToString("N")[..8]);
    }
}
