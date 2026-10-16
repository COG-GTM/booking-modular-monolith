namespace Unit.Test.Flight;

using System;
using FluentAssertions;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightCacheKeyTests
{
    [Fact]
    public void available_flights_query_and_update_command_share_the_invalidation_key()
    {
        var update = new UpdateFlight(
            Guid.NewGuid(),
            "FL123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2035, 1, 1, 8, 0, 0),
            new DateTime(2035, 1, 1, 10, 0, 0),
            Guid.NewGuid(),
            120,
            new DateTime(2035, 1, 1),
            global::Flight.Flights.Enums.FlightStatus.Flying,
            false,
            10
        );

        new GetAvailableFlights().CacheKey.Should().Be("GetAvailableFlights");
        update.CacheKey.Should().Be(new GetAvailableFlights().CacheKey);
    }
}
