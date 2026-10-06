using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Integration.Test.Fakes;
using Integration.Test.Routes;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserTests : IdentityIntegrationTestBase
{
    public RegisterNewUserTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_create_new_user_to_db_and_publish_message_to_broker()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response?.Should().NotBeNull();
        response?.Username.Should().Be(command.Username);

        (await Fixture.WaitForPublishing<UserCreated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_register_new_user_when_caller_is_admin()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();

        // Act
        var route = ApiRoutes.Identity.RegisterUser;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_return_forbidden_when_caller_is_not_admin()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();
        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Identity.RegisterUser;
        var result = await httpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}