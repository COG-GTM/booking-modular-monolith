using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Features.CreatingFlight.V1;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using global::Flight.Flights.Models;
using BuildingBlocks.Exception;
using MassTransit;

public class GetAvailableFlightsTests : FlightIntegrationTestBase
{
    public GetAvailableFlightsTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_return_available_flights()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetAvailableFlights();

        // Act
        var response = (await Fixture.SendAsync(query))?.FlightDtos?.ToList();

        // Assert
        response?.Should().NotBeNull();
        response?.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_return_at_most_one_page_without_deleted_flights()
    {
        // Arrange
        var active = Enumerable.Range(0, GetAvailableFlights.MaxPageSize + 20).Select(_ => NewFlight(false)).ToList();
        var deleted = Enumerable.Range(0, 50).Select(_ => NewFlight(true)).ToList();

        await Fixture.ExecuteReadContextAsync(db => db.Flight.InsertManyAsync(active.Concat(deleted)));

        var deletedIds = deleted.Select(x => x.FlightId).ToHashSet();

        // Act
        var firstPage = (await Fixture.SendAsync(new GetAvailableFlights(1, GetAvailableFlights.MaxPageSize)))
            .FlightDtos.ToList();
        var secondPage = (await Fixture.SendAsync(new GetAvailableFlights(2, GetAvailableFlights.MaxPageSize)))
            .FlightDtos.ToList();
        var defaultPage = (await Fixture.SendAsync(new GetAvailableFlights())).FlightDtos.ToList();

        // Assert
        firstPage.Should().HaveCount(GetAvailableFlights.MaxPageSize);
        defaultPage.Should().HaveCountLessThanOrEqualTo(GetAvailableFlights.DefaultPageSize);
        secondPage.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(GetAvailableFlights.MaxPageSize);
        firstPage.Select(x => x.Id).Should().NotIntersectWith(secondPage.Select(x => x.Id));
        firstPage.Concat(secondPage).Concat(defaultPage).Should().NotContain(x => deletedIds.Contains(x.Id));
    }

    [Fact]
    public async Task should_reject_page_size_above_maximum()
    {
        var act = () => Fixture.SendAsync(new GetAvailableFlights(1, GetAvailableFlights.MaxPageSize + 1));

        await act.Should().ThrowAsync<ValidationException>();
    }

    private static FlightReadModel NewFlight(bool isDeleted)
    {
        var id = NewId.NextGuid();
        return new FlightReadModel
        {
            Id = NewId.NextGuid(),
            FlightId = id,
            FlightNumber = "PG" + id.ToString("N")[..4],
            AircraftId = Guid.NewGuid(),
            DepartureDate = DateTime.UtcNow,
            DepartureAirportId = Guid.NewGuid(),
            ArriveDate = DateTime.UtcNow.AddHours(1),
            ArriveAirportId = Guid.NewGuid(),
            DurationMinutes = 60,
            FlightDate = DateTime.UtcNow,
            Status = global::Flight.Flights.Enums.FlightStatus.Flying,
            Price = 100,
            IsDeleted = isDeleted,
        };
    }
}
