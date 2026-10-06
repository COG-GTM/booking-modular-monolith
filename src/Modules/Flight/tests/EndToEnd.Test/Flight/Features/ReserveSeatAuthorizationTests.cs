using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Seats.Features.ReservingSeat.V1;
using FluentAssertions;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class ReserveSeatAuthorizationTests : FlightEndToEndTestBase
{
    public ReserveSeatAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_return_unauthorized_for_reserve_seat_without_token()
    {
        // Arrange
        var request = await CreateFlightWithSeatAsync();
        var anonymousClient = Fixture.HttpClient;
        anonymousClient.DefaultRequestHeaders.Authorization = null;

        // Act
        var result = await anonymousClient.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_forbid_reserve_seat_for_admin_without_api_scope()
    {
        // Arrange
        var request = await CreateFlightWithSeatAsync();
        var adminWithoutScopeClient = Fixture.HttpClient;
        adminWithoutScopeClient.SetFakeBearerToken(
            new Dictionary<string, object>
            {
                { ClaimTypes.Name, "test@sample.com" },
                { ClaimTypes.Role, IdentityConstant.Role.Admin },
            }
        );

        // Act
        var result = await adminWithoutScopeClient.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_not_reserve_seat_when_non_admin_request_is_forbidden()
    {
        // Arrange
        var request = await CreateFlightWithSeatAsync();
        var userClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var forbiddenResult = await userClient.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);
        var adminResult = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        // Assert
        forbiddenResult.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        adminResult.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_still_allow_non_admin_user_on_api_scope_only_endpoints()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(command);
        var userClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var route = ApiRoutes.Flight.GetFlightById.Replace(
            ApiRoutes.Flight.Id,
            command.Id.ToString(),
            StringComparison.CurrentCulture
        );
        var result = await userClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<ReserveSeatRequestDto> CreateFlightWithSeatAsync()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        return new ReserveSeatRequestDto(seatCommand.FlightId, seatCommand.SeatNumber);
    }
}
