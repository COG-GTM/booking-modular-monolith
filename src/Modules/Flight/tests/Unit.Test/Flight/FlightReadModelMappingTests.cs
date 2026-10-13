using Flight.Flights.Dtos;
using Flight.Flights.Models;
using FluentAssertions;
using MapsterMapper;
using MassTransit;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Flight;

// Regression tests for the read-model mappings fixed alongside the standalone Flight host:
// value objects must be flattened to their raw values and the DTO must expose the domain id,
// not the Mongo document id.
[Collection(nameof(UnitTestFixture))]
public class FlightReadModelMappingTests
{
    private readonly IMapper _mapper;

    public FlightReadModelMappingTests(UnitTestFixture fixture)
    {
        _mapper = fixture.Mapper;
    }

    [Fact]
    public void flight_to_read_model_should_store_the_raw_flight_number_and_flight_id()
    {
        var flight = FakeFlightCreate.Generate();

        var readModel = _mapper.Map<FlightReadModel>(flight);

        readModel.FlightNumber.Should().Be(flight.FlightNumber.Value);
        readModel.FlightNumber.Should().NotContain("Value");
        readModel.FlightId.Should().Be(flight.Id.Value);
        readModel.Id.Should().NotBeEmpty();
        readModel.Id.Should().NotBe(flight.Id.Value);
    }

    [Fact]
    public void read_model_to_dto_should_expose_the_flight_id_not_the_document_id()
    {
        var readModel = new FlightReadModel
        {
            Id = NewId.NextGuid(),
            FlightId = NewId.NextGuid(),
            FlightNumber = "BD467",
            AircraftId = NewId.NextGuid(),
            DepartureDate = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            DepartureAirportId = NewId.NextGuid(),
            ArriveDate = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ArriveAirportId = NewId.NextGuid(),
            DurationMinutes = 120,
            FlightDate = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = global::Flight.Flights.Enums.FlightStatus.Flying,
            Price = 8000,
            IsDeleted = false,
        };

        var dto = _mapper.Map<FlightDto>(readModel);

        dto.Id.Should().Be(readModel.FlightId);
        dto.FlightNumber.Should().Be("BD467");
        dto.AircraftId.Should().Be(readModel.AircraftId);
        dto.Price.Should().Be(8000);
    }

    [Fact]
    public void flight_round_trip_through_the_read_model_should_preserve_identity_and_flight_number()
    {
        var flight = FakeFlightCreate.Generate();

        var dto = _mapper.Map<FlightDto>(_mapper.Map<FlightReadModel>(flight));

        dto.Id.Should().Be(flight.Id.Value);
        dto.FlightNumber.Should().Be(flight.FlightNumber.Value);
    }
}
