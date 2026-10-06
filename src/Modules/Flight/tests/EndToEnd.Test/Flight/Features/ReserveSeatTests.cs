using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Seats.Features.ReservingSeat.V1;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class ReserveSeatTests : FlightEndToEndTestBase
{
    public ReserveSeatTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_forbid_reserve_seat_for_non_admin_user()
    {
        // Arrange
        var request = await CreateFlightWithSeat();
        var userClient = Fixture.CreateHttpClient(IdentityConstant.Role.User);

        // Act
        var result = await userClient.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_reserve_seat_for_admin_user()
    {
        // Arrange
        var request = await CreateFlightWithSeat();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<ReserveSeatRequestDto> CreateFlightWithSeat()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        return new ReserveSeatRequestDto(seatCommand.FlightId, seatCommand.SeatNumber);
    }
}
