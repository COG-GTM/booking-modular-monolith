using System;
using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Seat.Features;

using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;
using global::Flight.Seats.Features.ReservingSeat.V1;

public class GetAvailableSeatsTests : FlightIntegrationTestBase
{
    public GetAvailableSeatsTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_return_available_seats_from_grpc_service()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        response?.Should().NotBeNull();
        response?.SeatDtos?.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_map_seat_fields_in_grpc_response()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        response.Should().NotBeNull();
        response.SeatDtos.Should().HaveCount(1);

        var seat = response.SeatDtos.Single();
        seat.Id.Should().NotBeNullOrEmpty();
        seat.SeatNumber.Should().Be(seatCommand.SeatNumber);
        seat.Type.Should().Be(SeatType.Middle);
        seat.Class.Should().Be(SeatClass.Economy);
        seat.FlightId.Should().Be(flightCommand.Id.ToString());
    }

    [Fact]
    public async Task should_exclude_reserved_seats_from_query_result()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var availableSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();
        var reservedSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(availableSeatCommand);
        await Fixture.SendAsync(reservedSeatCommand);

        await Fixture.SendAsync(new ReserveSeatMongo(
            reservedSeatCommand.Id,
            reservedSeatCommand.SeatNumber,
            reservedSeatCommand.Type,
            reservedSeatCommand.Class,
            reservedSeatCommand.FlightId,
            IsDeleted: true));

        // Act
        var response = await Fixture.SendAsync(new GetAvailableSeats(flightCommand.Id));

        // Assert
        response.Should().NotBeNull();
        response.SeatDtos.Should().HaveCount(1);

        var seat = response.SeatDtos.Single();
        seat.SeatNumber.Should().Be(availableSeatCommand.SeatNumber);
        seat.Type.Should().Be(availableSeatCommand.Type);
        seat.Class.Should().Be(availableSeatCommand.Class);
        seat.FlightId.Should().Be(flightCommand.Id);
    }

    [Fact]
    public async Task should_exclude_reserved_seats_from_grpc_response()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var availableSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();
        var reservedSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(availableSeatCommand);
        await Fixture.SendAsync(reservedSeatCommand);

        await Fixture.SendAsync(new ReserveSeatMongo(
            reservedSeatCommand.Id,
            reservedSeatCommand.SeatNumber,
            reservedSeatCommand.Type,
            reservedSeatCommand.Class,
            reservedSeatCommand.FlightId,
            IsDeleted: true));

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        response.Should().NotBeNull();
        response.SeatDtos.Should().HaveCount(1);
        response.SeatDtos.Single().SeatNumber.Should().Be(availableSeatCommand.SeatNumber);
        response.SeatDtos.Should().NotContain(x => x.SeatNumber == reservedSeatCommand.SeatNumber);
    }

    [Fact]
    public async Task should_throw_all_seats_full_exception_when_flight_has_no_seats()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        // Act
        var act = async () => { await Fixture.SendAsync(new GetAvailableSeats(flightCommand.Id)); };

        // Assert
        await act.Should().ThrowAsync<AllSeatsFullException>();
    }

    [Fact]
    public async Task should_throw_all_seats_full_exception_when_all_seats_are_reserved()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        await Fixture.SendAsync(new ReserveSeatMongo(
            seatCommand.Id,
            seatCommand.SeatNumber,
            seatCommand.Type,
            seatCommand.Class,
            seatCommand.FlightId,
            IsDeleted: true));

        // Act
        var act = async () => { await Fixture.SendAsync(new GetAvailableSeats(flightCommand.Id)); };

        // Assert
        await act.Should().ThrowAsync<AllSeatsFullException>();
    }

    [Fact]
    public async Task should_surface_all_seats_full_as_rpc_exception_from_grpc_service()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var act = async () =>
        {
            await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });
        };

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Internal);
        exception.Which.Status.Detail.Should().Be(new AllSeatsFullException().Message);
    }
}
