using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Flights.Features.CreatingFlight.V1;
using Flight.Seats.Features.CreatingSeat.V1;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EndToEnd.Test.Seat.Features;

public class ReserveSeatTests : FlightEndToEndTestBase
{
    public ReserveSeatTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_return_forbidden_and_keep_seat_available_when_user_is_not_admin()
    {
        var (flightCommand, seatCommand) = await CreateFlightAndSeatAsync();

        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);
        var result = await client.PostAsJsonAsync(
            ApiRoutes.Flight.ReserveSeat,
            new { FlightId = flightCommand.Id, seatCommand.SeatNumber }
        );

        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var seatIsAvailable = await Fixture.ExecuteDbContextAsync(db =>
            db.Seats.AnyAsync(
                seat =>
                    seat.FlightId.Value == flightCommand.Id
                    && seat.SeatNumber.Value == seatCommand.SeatNumber
                    && !seat.IsDeleted
            )
        );

        seatIsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_reserve_seat_when_user_is_admin()
    {
        var (flightCommand, seatCommand) = await CreateFlightAndSeatAsync();

        var result = await Fixture.HttpClient.PostAsJsonAsync(
            ApiRoutes.Flight.ReserveSeat,
            new { FlightId = flightCommand.Id, seatCommand.SeatNumber }
        );

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(CreateFlight FlightCommand, CreateSeat SeatCommand)> CreateFlightAndSeatAsync()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        return (flightCommand, seatCommand);
    }
}
