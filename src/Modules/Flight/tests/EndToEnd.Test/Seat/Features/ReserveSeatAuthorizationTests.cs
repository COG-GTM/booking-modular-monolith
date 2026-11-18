using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Seats.Features.CreatingSeat.V1;
using Flight.Seats.Features.ReservingSeat.V1;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EndToEnd.Test.Seat.Features;

public class ReserveSeatAuthorizationTests : FlightEndToEndTestBase
{
    public ReserveSeatAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_forbid_reserve_seat_for_user_role_and_keep_seat_available()
    {
        // Arrange
        var seat = await CreateSeatAsync();

        // Act
        var result = await Fixture.CreateHttpClient(IdentityConstant.Role.User)
            .PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, new ReserveSeatRequestDto(seat.FlightId, seat.SeatNumber));

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IsSeatAvailableAsync(seat)).Should().BeTrue();
    }

    [Fact]
    public async Task should_reserve_seat_for_admin_role()
    {
        // Arrange
        var seat = await CreateSeatAsync();

        // Act
        var result = await Fixture.CreateHttpClient(IdentityConstant.Role.Admin)
            .PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, new ReserveSeatRequestDto(seat.FlightId, seat.SeatNumber));

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IsSeatAvailableAsync(seat)).Should().BeFalse();
    }

    private async Task<CreateSeat> CreateSeatAsync()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        return seatCommand;
    }

    private Task<bool> IsSeatAvailableAsync(CreateSeat seat) =>
        Fixture.ExecuteDbContextAsync(db => db.Seats.AnyAsync(
            x => x.SeatNumber.Value == seat.SeatNumber && x.FlightId == seat.FlightId));
}
