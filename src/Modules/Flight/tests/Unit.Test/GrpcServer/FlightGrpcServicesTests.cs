namespace Unit.Test.GrpcServer;

using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Grpc.Flight;
using FluentAssertions;
using global::Flight.Flights.Dtos;
using global::Flight.Flights.Features.GettingFlightById.V1;
using global::Flight.Seats.Dtos;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;
using global::Flight.Seats.Features.ReservingSeat.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using NSubstitute;
using Unit.Test.Common;
using Xunit;
using AvailableSeatsResult = global::Flight.Seats.Features.GettingAvailableSeats.V1.GetAvailableSeatsResult;
using FlightByIdResult = global::Flight.Flights.Features.GettingFlightById.V1.GetFlightByIdResult;
using FlightStatus = global::Flight.Flights.Enums.FlightStatus;
using GrpcFlightResult = Contracts.Grpc.Flight.GetFlightByIdResult;
using GrpcFlightStatus = Contracts.Grpc.Flight.FlightStatus;
using GrpcReserveResult = Contracts.Grpc.Flight.ReserveSeatResult;
using SeatClass = global::Flight.Seats.Enums.SeatClass;
using SeatReserveResult = global::Flight.Seats.Features.ReservingSeat.V1.ReserveSeatResult;
using SeatType = global::Flight.Seats.Enums.SeatType;

[Collection(nameof(UnitTestFixture))]
public class FlightGrpcServicesTests
{
    public FlightGrpcServicesTests(UnitTestFixture fixture)
    {
        _ = fixture.Mapper;
    }

    [Fact]
    public async Task get_by_id_sends_id_and_maps_the_flight_fields()
    {
        var mediator = Substitute.For<IMediator>();
        var id = Guid.NewGuid();
        var departureDate = new DateTime(2035, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var dto = new FlightDto(
            id,
            "FL123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            departureDate,
            departureDate.AddHours(2),
            Guid.NewGuid(),
            120,
            departureDate.Date,
            FlightStatus.Flying,
            250.5m
        );
        mediator
            .Send(Arg.Any<GetFlightById>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlightByIdResult(dto)));
        var service = new global::Flight.GrpcServer.Services.FlightGrpcServices(mediator);

        var result = await service.GetById(new GetByIdRequest { Id = id.ToString() }, null!);

        result.Should().BeOfType<GrpcFlightResult>();
        result.FlightDto.Id.Should().Be(id.ToString());
        result.FlightDto.FlightNumber.Should().Be("FL123");
        result.FlightDto.DurationMinutes.Should().Be(120);
        result.FlightDto.Price.Should().Be(250.5);
        result.FlightDto.Status.Should().Be(GrpcFlightStatus.Flying);
        await mediator.Received(1).Send(Arg.Is<GetFlightById>(query => query.Id == id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task get_available_seats_maps_each_seat_to_the_grpc_response()
    {
        var mediator = Substitute.For<IMediator>();
        var flightId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        mediator
            .Send(Arg.Any<GetAvailableSeats>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new AvailableSeatsResult([new SeatDto(seatId, "12A", SeatType.Window, SeatClass.Economy, flightId)])
                )
            );
        var service = new global::Flight.GrpcServer.Services.FlightGrpcServices(mediator);

        var result = await service.GetAvailableSeats(
            new GetAvailableSeatsRequest { FlightId = flightId.ToString() },
            null!
        );

        result.SeatDtos.Should().ContainSingle();
        result.SeatDtos[0].Id.Should().Be(seatId.ToString());
        result.SeatDtos[0].FlightId.Should().Be(flightId.ToString());
        result.SeatDtos[0].SeatNumber.Should().Be("12A");
    }

    [Fact]
    public async Task get_available_seats_returns_empty_result_when_mediator_returns_null()
    {
        var mediator = Substitute.For<IMediator>();
        mediator
            .Send(Arg.Any<GetAvailableSeats>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AvailableSeatsResult>(null!));
        var service = new global::Flight.GrpcServer.Services.FlightGrpcServices(mediator);

        var result = await service.GetAvailableSeats(
            new GetAvailableSeatsRequest { FlightId = Guid.NewGuid().ToString() },
            null!
        );

        result.SeatDtos.Should().BeEmpty();
    }

    [Fact]
    public async Task reserve_seat_sends_flight_and_seat_and_returns_id_string()
    {
        var mediator = Substitute.For<IMediator>();
        var flightId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        mediator
            .Send(Arg.Any<ReserveSeat>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SeatReserveResult(seatId)));
        var service = new global::Flight.GrpcServer.Services.FlightGrpcServices(mediator);

        var result = await service.ReserveSeat(
            new ReserveSeatRequest { FlightId = flightId.ToString(), SeatNumber = "12A" },
            null!
        );

        result.Should().BeOfType<GrpcReserveResult>();
        result.Id.Should().Be(seatId.ToString());
        await mediator
            .Received(1)
            .Send(
                Arg.Is<ReserveSeat>(command => command.FlightId == flightId && command.SeatNumber == "12A"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task invalid_guid_in_request_should_throw_format_exception()
    {
        var mediator = Substitute.For<IMediator>();
        var service = new global::Flight.GrpcServer.Services.FlightGrpcServices(mediator);

        var act = () => service.GetById(new GetByIdRequest { Id = "bad-guid" }, null!);

        await act.Should().ThrowAsync<FormatException>();
    }
}
