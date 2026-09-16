using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Features.RegisteringNewUser.V1;
using Xunit;

namespace EndToEnd.Test.Identity.Features;

public class RegisterNewUserTests : IdentityEndToEndTestBase
{
    public RegisterNewUserTests(TestWriteFixture<Program, IdentityContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_register_new_user_through_http_and_publish_message_to_broker()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        response.Should().NotBeNull();
        response!.Username.Should().Be(request.Username);
        response.PassportNumber.Should().Be(request.PassportNumber);
        response.Id.Should().NotBeEmpty();

        (await Fixture.WaitForPublishing<UserCreated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_return_bad_request_when_passwords_do_not_match()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate() with
        {
            ConfirmPassword = "Different@123",
        };

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_return_bad_request_when_username_already_exists()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate();
        await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        var duplicate = request with
        {
            Email = "other_" + request.Email,
            PassportNumber = "X" + request.PassportNumber,
        };

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, duplicate);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
