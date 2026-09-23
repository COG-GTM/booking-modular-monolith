using AutoBogus;

namespace Integration.Test.Fakes;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class FakeRegisterNewUserRequestDto : AutoFaker<RegisterNewUserRequestDto>
{
    public FakeRegisterNewUserRequestDto()
    {
        RuleFor(r => r.Username, _ => "TestMyEndpointUser");
        RuleFor(r => r.Password, _ => "Password@123");
        RuleFor(r => r.ConfirmPassword, _ => "Password@123");
        RuleFor(r => r.Email, _ => "endpoint@test.com");
    }
}
