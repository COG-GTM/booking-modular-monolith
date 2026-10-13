using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Integration.Test.Seat.Features;

using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.ReservingSeat.V1;

public class ReserveSeatConflictTests : FlightIntegrationTestBase
{
    public ReserveSeatConflictTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task reserving_same_seat_twice_over_grpc_should_reject_second_reservation()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);
        var request = new ReserveSeatRequest
        {
            FlightId = seatCommand.FlightId.ToString(),
            SeatNumber = seatCommand.SeatNumber,
        };

        var first = await flightGrpcClient.ReserveSeatAsync(request);
        first.Id.Should().Be(seatCommand.Id.ToString());

        // Act
        var act = async () => await flightGrpcClient.ReserveSeatAsync(request);

        // Assert
        (await act.Should().ThrowAsync<RpcException>())
            .Which.Status.Detail.Should()
            .Be(new SeatAlreadyReservedException().Message);
    }

    [Fact]
    public async Task reserving_same_seat_twice_over_rest_should_return_409_conflict()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        var request = new ReserveSeatRequestDto(seatCommand.FlightId, seatCommand.SeatNumber);
        var client = Fixture.HttpClient;

        var first = await client.PostAsJsonAsync("api/v1/flight/reserve-seat", request);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var second = await client.PostAsJsonAsync("api/v1/flight/reserve-seat", request);

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.Conflict);
        problem.Title.Should().Be(nameof(SeatAlreadyReservedException));
    }
}
