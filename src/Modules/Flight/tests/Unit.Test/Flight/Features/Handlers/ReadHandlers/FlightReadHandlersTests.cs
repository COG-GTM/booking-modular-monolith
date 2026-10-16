namespace Unit.Test.Flight.Features.Handlers.ReadHandlers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Dtos;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using global::Flight.Flights.Features.GettingFlightById.V1;
using global::Flight.Flights.Models;
using global::Flight.Seats.Dtos;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;
using global::Flight.Seats.Models;
using MapsterMapper;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightReadHandlersTests
{
    private readonly IMapper _mapper;

    public FlightReadHandlersTests(UnitTestFixture fixture)
    {
        _mapper = fixture.Mapper;
    }

    [Fact]
    public async Task get_available_flights_filters_deleted_documents_and_maps_flight_id()
    {
        var active = FlightDocument(Guid.NewGuid(), false);
        var deleted = FlightDocument(Guid.NewGuid(), true);
        using var context = FakeFlightReadDbContext.Create([active, deleted]);
        var handler = new GetAvailableFlightsHandler(_mapper, context);

        var result = await handler.Handle(new GetAvailableFlights(), CancellationToken.None);

        result.FlightDtos.Should().ContainSingle();
        result.FlightDtos.Single().Id.Should().Be(active.FlightId);
        result.FlightDtos.Single().FlightNumber.Should().Be(active.FlightNumber);
    }

    [Fact]
    public async Task get_available_flights_with_no_active_documents_should_throw_not_found()
    {
        using var context = FakeFlightReadDbContext.Create([FlightDocument(Guid.NewGuid(), true)]);
        var handler = new GetAvailableFlightsHandler(_mapper, context);

        var act = () => handler.Handle(new GetAvailableFlights(), CancellationToken.None);

        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task get_available_flights_with_an_empty_cursor_should_throw_not_found()
    {
        using var context = FakeFlightReadDbContext.Create(Array.Empty<FlightReadModel>());
        var handler = new GetAvailableFlightsHandler(_mapper, context);

        var act = () => handler.Handle(new GetAvailableFlights(), CancellationToken.None);

        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task get_flight_by_id_maps_a_document_returned_by_the_cursor()
    {
        var document = FlightDocument(Guid.NewGuid(), false);
        PipelineDefinition<FlightReadModel, FlightReadModel>? pipeline = null;
        using var context = FakeFlightReadDbContext.Create(
            [document],
            captureFlightPipeline: capturedPipeline => pipeline = capturedPipeline
        );
        var handler = new GetFlightByIdHandler(_mapper, context);

        var result = await handler.Handle(new GetFlightById(document.FlightId), CancellationToken.None);

        result.FlightDto.Id.Should().Be(document.FlightId);
        result.FlightDto.FlightNumber.Should().Be(document.FlightNumber);

        var rendered = pipeline!
            .Render(
                new RenderArgs<FlightReadModel>(
                    BsonSerializer.LookupSerializer<FlightReadModel>(),
                    BsonSerializer.SerializerRegistry
                )
            )
            .Documents;
        var match = rendered.Should().ContainSingle(stage => stage.Contains("$match")).Which["$match"].AsBsonDocument;
        match["flightId"].Should().Be(document.ToBsonDocument()["flightId"]);
        match["isDeleted"].AsBsonDocument["$ne"].Should().Be(BsonBoolean.True);
    }

    [Fact]
    public async Task get_flight_by_id_with_an_empty_cursor_should_throw_not_found()
    {
        using var context = FakeFlightReadDbContext.Create(Array.Empty<FlightReadModel>());
        var handler = new GetFlightByIdHandler(_mapper, context);

        var act = () => handler.Handle(new GetFlightById(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task get_available_seats_filters_other_flights_and_deleted_seats()
    {
        var flightId = Guid.NewGuid();
        var available = SeatDocument(Guid.NewGuid(), flightId, false);
        var otherFlight = SeatDocument(Guid.NewGuid(), Guid.NewGuid(), false);
        var reserved = SeatDocument(Guid.NewGuid(), flightId, true);
        using var context = FakeFlightReadDbContext.Create(seats: [available, otherFlight, reserved]);
        var handler = new GetAvailableSeatsQueryHandler(_mapper, context);

        var result = await handler.Handle(new GetAvailableSeats(flightId), CancellationToken.None);

        result.SeatDtos.Should().ContainSingle();
        result.SeatDtos.Single().Id.Should().Be(available.SeatId);
        result.SeatDtos.Single().FlightId.Should().Be(flightId);
    }

    [Fact]
    public async Task get_available_seats_with_no_available_documents_should_throw_all_seats_full()
    {
        var flightId = Guid.NewGuid();
        using var context = FakeFlightReadDbContext.Create(
            seats: [SeatDocument(Guid.NewGuid(), flightId, true), SeatDocument(Guid.NewGuid(), Guid.NewGuid(), false)]
        );
        var handler = new GetAvailableSeatsQueryHandler(_mapper, context);

        var act = () => handler.Handle(new GetAvailableSeats(flightId), CancellationToken.None);

        await act.Should().ThrowAsync<AllSeatsFullException>();
    }

    private static FlightReadModel FlightDocument(Guid id, bool isDeleted) =>
        new()
        {
            Id = Guid.NewGuid(),
            FlightId = id,
            FlightNumber = "FL123",
            AircraftId = Guid.NewGuid(),
            DepartureDate = new DateTime(2035, 1, 1, 8, 0, 0),
            DepartureAirportId = Guid.NewGuid(),
            ArriveDate = new DateTime(2035, 1, 1, 10, 0, 0),
            ArriveAirportId = Guid.NewGuid(),
            DurationMinutes = 120,
            FlightDate = new DateTime(2035, 1, 1),
            Status = FlightStatus.Flying,
            Price = 200,
            IsDeleted = isDeleted,
        };

    private static SeatReadModel SeatDocument(Guid id, Guid flightId, bool isDeleted) =>
        new()
        {
            Id = Guid.NewGuid(),
            SeatId = id,
            SeatNumber = "12A",
            Type = SeatType.Window,
            Class = SeatClass.Economy,
            FlightId = flightId,
            IsDeleted = isDeleted,
        };
}
