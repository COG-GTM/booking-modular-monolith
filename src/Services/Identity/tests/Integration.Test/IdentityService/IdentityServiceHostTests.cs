using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Features.RegisteringNewUser.V1;
using IdentityService;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.IdentityServiceHost;

public class IdentityServiceHostTests : IdentityServiceIntegrationTestBase
{
    public IdentityServiceHostTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_boot_standalone_host_and_expose_root_endpoint_with_service_name()
    {
        // Act
        var response = await Fixture.HttpClient.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Identity-Service");
    }

    [Fact]
    public async Task should_expose_identity_server_discovery_document()
    {
        // Act
        var response = await Fixture.HttpClient.GetAsync("/.well-known/openid-configuration");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("issuer");
    }

    [Fact]
    public async Task should_register_new_user_through_standalone_host_endpoint()
    {
        // Arrange
        var request = new RegisterNewUserRequestDto(
            "TestFirstName",
            "TestLastName",
            "TestServiceUser",
            "test-service@test.com",
            "Password@123",
            "Password@123",
            "12345678"
        );

        // Act
        var response = await Fixture.HttpClient.PostAsJsonAsync("api/v1/identity/register-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        result?.Username.Should().Be(request.Username);

        var user = await Fixture.ExecuteDbContextAsync(db =>
            db.Users.FirstOrDefaultAsync(u => u.UserName == request.Username)
        );
        user.Should().NotBeNull();

        (await Fixture.WaitForPublishing<UserCreated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_reject_unauthenticated_request_to_protected_endpoint()
    {
        // Arrange
        var request = new RegisterNewUserRequestDto(
            "TestFirstName",
            "TestLastName",
            "UnauthorizedUser",
            "unauthorized@test.com",
            "Password@123",
            "Password@123",
            "12345678"
        );

        var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.PostAsJsonAsync("api/v1/identity/register-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
