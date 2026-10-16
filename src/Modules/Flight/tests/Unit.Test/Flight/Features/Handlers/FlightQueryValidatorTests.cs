namespace Unit.Test.Flight.Features.Handlers;

using System;
using FluentAssertions;
using global::Flight.Flights.Features.GettingFlightById.V1;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightQueryValidatorTests
{
    [Fact]
    public void get_flight_by_id_validator_accepts_nonempty_id()
    {
        new GetFlightByIdValidator().Validate(new GetFlightById(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void get_available_seats_validator_accepts_nonempty_flight_id()
    {
        new GetAvailableSeatsValidator().Validate(new GetAvailableSeats(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
