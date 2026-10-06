using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Identity.Host.Integration.Test;
using Identity.Host.Integration.Test.Fakes;
using Xunit;
using RegisterNewUser = global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUser;
using RegisterNewUserRequestDto = global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUserRequestDto;
using RegisterNewUserResponseDto = global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUserResponseDto;

namespace Identity.Host.Integration.Test.Identity.Features;

public class RegisterNewUserTests(
    BuildingBlocks.TestBase.TestWriteFixture<
        global::Identity.Host.Program,
        global::Identity.Data.IdentityContext
    > integrationTestFactory
) : IdentityHostIntegrationTestBase(integrationTestFactory)
{
    [Fact]
    public async Task should_create_new_user_and_publish_user_created_to_broker()
    {
        await EnsureUserRoleExistsAsync();
        var command = new FakeRegisterNewUserCommand().Generate();

        var response = await Fixture.SendAsync(command);

        response?.Username.Should().Be(command.Username);
        (await Fixture.WaitForPublishing<UserCreated>()).Should().BeTrue();
    }

    [Fact]
    public async Task should_register_user_via_http_endpoint()
    {
        await EnsureUserRoleExistsAsync();
        var uniqueId = Guid.NewGuid().ToString("N");
        var username = $"identity-host-http-{uniqueId}";
        var request = new RegisterNewUserRequestDto(
            "Http",
            "Endpoint",
            username,
            $"{username}@test.com",
            "Password@123",
            "Password@123",
            uniqueId[..12]
        );

        using var httpClient = Fixture.HttpClient;
        using var response = await httpClient.PostAsJsonAsync("/api/v1/identity/register-user", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        result.Should().NotBeNull();
        result!.Username.Should().Be(username);
        (await Fixture.WaitForPublishing<UserCreated>()).Should().BeTrue();
    }

    [Fact]
    public async Task should_issue_access_token_for_registered_user()
    {
        await EnsureUserRoleExistsAsync();
        var command = new RegisterNewUser(
            "Token",
            "User",
            "identity-host-token-user",
            "identity-host-token-user@test.com",
            "Password@123",
            "Password@123",
            "87654321"
        );

        await Fixture.SendAsync(command);

        using var httpClient = Fixture.HttpClient;
        using var response = await httpClient.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = command.Username,
                    ["password"] = command.Password,
                    ["scope"] = "booking-modular-monolith",
                }
            )
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var tokenResponse = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        tokenResponse.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
