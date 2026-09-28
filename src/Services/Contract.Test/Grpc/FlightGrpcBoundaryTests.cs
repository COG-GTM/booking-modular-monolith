using System.Threading.Tasks;
using BookingFlight;
using BuildingBlocks.TestBase;
using Contract.Test.Fakes;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Contract.Test.Grpc;

/// <summary>
/// Drives the real Flight host (Testcontainers-backed) through Booking's generated client stub, i.e. exactly the
/// call path Booking.Api uses at runtime once the two run as separate processes.
/// </summary>
public class FlightGrpcBoundaryTests : FlightGrpcContractTestBase
{
    public FlightGrpcBoundaryTests(TestFixture<Flight.Api.Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    private FlightGrpcService.FlightGrpcServiceClient Client => new(Fixture.Channel);

    [Fact]
    public async Task booking_client_can_get_flight_by_id()
    {
        var command = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(command);

        var response = await Client.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        response.FlightDto.Should().NotBeNull();
        response.FlightDto.Id.Should().Be(command.Id.ToString());
        response.FlightDto.FlightNumber.Should().Be(command.FlightNumber);
    }

    [Fact]
    public async Task booking_client_can_get_available_seats_and_reserve_one()
    {
        var flight = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(flight);

        var seat = new FakeCreateSeatMongoCommand(flight.Id).Generate();
        await Fixture.SendAsync(seat);

        var seats = await Client.GetAvailableSeatsAsync(
            new GetAvailableSeatsRequest { FlightId = flight.Id.ToString() }
        );

        seats.SeatDtos.Should().ContainSingle(s => s.SeatNumber == seat.SeatNumber);

        var reserved = await Client.ReserveSeatAsync(
            new ReserveSeatRequest { FlightId = flight.Id.ToString(), SeatNumber = seat.SeatNumber }
        );

        reserved.Should().NotBeNull();
    }

    [Fact]
    public async Task unknown_flight_surfaces_as_a_grpc_status_not_a_transport_failure()
    {
        var act = () => Client.GetByIdAsync(new GetByIdRequest { Id = System.Guid.NewGuid().ToString() }).ResponseAsync;

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().NotBe(StatusCode.Unavailable);
        exception.Which.StatusCode.Should().NotBe(StatusCode.Unimplemented);
    }
}
