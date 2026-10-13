using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Data.Seed;
using FluentAssertions;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class AdminEndpointAuthorizationTests : FlightEndToEndTestBase
{
    public AdminEndpointAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    public static IEnumerable<object[]> AdminMutationRoutes =>
        new List<object[]>
        {
            new object[] { "POST", ApiRoutes.Flight.CreateFlight },
            new object[] { "PUT", ApiRoutes.Flight.UpdateFlight },
            new object[]
            {
                "DELETE",
                ApiRoutes.Flight.DeleteFlight.Replace(
                    ApiRoutes.Flight.Id,
                    InitialData.Flights.First().Id.ToString(),
                    StringComparison.Ordinal
                ),
            },
            new object[] { "POST", ApiRoutes.Flight.CreateAircraft },
            new object[] { "POST", ApiRoutes.Flight.CreateAirport },
            new object[] { "POST", ApiRoutes.Flight.CreateSeat },
            new object[] { "POST", ApiRoutes.Identity.RegisterUser },
        };

    [Theory]
    [MemberData(nameof(AdminMutationRoutes))]
    public async Task should_return_forbidden_for_admin_endpoints_when_user_is_not_admin(string method, string route)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Content = JsonContent.Create(new { });

        // Act
        var result = await Fixture.UserHttpClient.SendAsync(request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_allow_user_to_read_flight_by_id()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(command);
        var route = ApiRoutes.Flight.GetFlightById.Replace(
            ApiRoutes.Flight.Id,
            command.Id.ToString(),
            StringComparison.Ordinal
        );

        // Act
        var result = await Fixture.UserHttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task should_allow_admin_to_create_aircraft()
    {
        // Arrange
        var command = new FakeCreateAircraftCommand().Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Flight.CreateAircraft, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task should_allow_admin_to_register_new_user()
    {
        // Arrange
        await EnsureRoleExistsAsync(IdentityConstant.Role.User);
        var request = new FakeRegisterNewUserRequest().Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        if (await roleManager.RoleExistsAsync(role) == false)
        {
            await roleManager.CreateAsync(new Role { Name = role });
        }
    }
}
