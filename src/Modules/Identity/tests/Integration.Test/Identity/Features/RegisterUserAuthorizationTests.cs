using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Identity.Features;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class RegisterUserAuthorizationTests(TestWriteFixture<Program, IdentityContext> fixture)
    : IdentityIntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(IdentityConstant.Role.User, HttpStatusCode.Forbidden)]
    [InlineData(IdentityConstant.Role.Admin, HttpStatusCode.OK)]
    public async Task should_require_admin_to_register_a_user(string role, HttpStatusCode expectedStatus)
    {
        var command = new FakeRegisterNewUserCommand().Generate();
        var request = new RegisterNewUserRequestDto(
            command.FirstName,
            command.LastName,
            command.Username,
            command.Email,
            command.Password,
            command.ConfirmPassword,
            command.PassportNumber
        );
        using var client = Fixture.CreateHttpClient(role);

        using var response = await client.PostAsJsonAsync("api/v1/identity/register-user", request);

        response.StatusCode.Should().Be(expectedStatus);
    }
}
