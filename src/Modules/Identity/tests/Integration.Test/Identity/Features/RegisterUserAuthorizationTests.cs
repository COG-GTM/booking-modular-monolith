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

public class RegisterUserAuthorizationTests : IdentityIntegrationTestBase
{
    public RegisterUserAuthorizationTests(TestWriteFixture<Program, IdentityContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task user_should_not_be_allowed_to_register_users()
    {
        var client = Fixture.CreateHttpClient(IdentityConstant.Role.User, Guid.NewGuid());
        var command = new FakeRegisterNewUserCommand().Generate();
        var response = await client.PostAsJsonAsync("api/v1.0/identity/register-user", command);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
