using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Features.RegisteringNewUser.V1;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Host.Test;

public class IdentityHostTests : IdentityHostTestBase
{
    public IdentityHostTests(TestWriteFixture<Program, IdentityContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task register_user_endpoint_persists_user_and_publishes_user_created()
    {
        await EnsureUserRoleAsync();
        var request = new RegisterNewUserRequestDto(
            "Host",
            "User",
            "host-user",
            "host-user@test.com",
            "Password@123",
            "Password@123",
            "H12345"
        );

        var response = await Fixture.HttpClient.PostAsJsonAsync("api/v1/identity/register-user", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        body!.Username.Should().Be("host-user");
        (await Fixture.WaitForPublishing<UserCreated>()).Should().BeTrue();
    }

    [Fact]
    public async Task token_endpoint_issues_access_token_for_booking_audience()
    {
        await EnsureUserRoleAsync();
        using (var scope = Fixture.ServiceProvider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            if (await users.FindByNameAsync("token-user") is null)
            {
                var user = new User
                {
                    UserName = "token-user",
                    Email = "token-user@test.com",
                    FirstName = "Token",
                    LastName = "User",
                    PassPortNumber = "T12345",
                };
                (await users.CreateAsync(user, "Password@123")).Succeeded.Should().BeTrue();
                await users.AddToRoleAsync(user, IdentityConstant.Role.User);
            }
        }

        var response = await Fixture.HttpClient.PostAsync(
            "connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = "token-user",
                    ["password"] = "Password@123",
                    ["scope"] = "booking-modular-monolith role",
                }
            )
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("scope").GetString().Should().Contain("booking-modular-monolith");
    }

    [Fact]
    public async Task discovery_document_advertises_configured_issuer()
    {
        var expected = Fixture.Configuration["AuthOptions:IssuerUri"];

        using var json = JsonDocument.Parse(
            await Fixture.HttpClient.GetStringAsync(".well-known/openid-configuration")
        );

        json.RootElement.GetProperty("issuer").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task health_probes_report_live_and_ready()
    {
        var live = await Fixture.HttpClient.GetAsync("health/live");
        var ready = await Fixture.HttpClient.GetAsync("health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        var checks = await ready.Content.ReadAsStringAsync();
        checks.Should().Contain("postgres-identity").And.Contain("postgres-identity-persist-message");
    }

    [Fact]
    public void host_composes_only_identity_endpoints()
    {
        var routes = Fixture
            .ServiceProvider.GetServices<EndpointDataSource>()
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(x => x.RoutePattern.RawText ?? string.Empty)
            .Where(x => x.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        routes.Should().NotBeEmpty();
        routes.Should().OnlyContain(x => x.Contains("/identity/", StringComparison.OrdinalIgnoreCase));
        AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(x => x.GetName().Name)
            .Should()
            .NotContain(["Flight", "Passenger", "Booking", "Api"]);
    }

    private async Task EnsureUserRoleAsync()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        if (!await roles.RoleExistsAsync(IdentityConstant.Role.User))
            await roles.CreateAsync(new Role { Name = IdentityConstant.Role.User });
    }
}
