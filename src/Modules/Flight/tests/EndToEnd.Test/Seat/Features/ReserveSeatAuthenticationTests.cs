using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Seats.ValueObjects;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Seat.Features;

public class ReserveSeatAuthenticationTests : FlightEndToEndTestBase
{
    public ReserveSeatAuthenticationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_return_unauthorized_and_keep_seat_available_when_user_is_anonymous()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;

        var result = await client.PostAsJsonAsync(
            ApiRoutes.Flight.ReserveSeat,
            new { FlightId = flightCommand.Id, seatCommand.SeatNumber }
        );

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var seat = await Fixture.FindAsync<global::Flight.Seats.Models.Seat, SeatId>(SeatId.Of(seatCommand.Id));

        seat.Should().NotBeNull();
        seat.IsDeleted.Should().BeFalse();
    }
}
