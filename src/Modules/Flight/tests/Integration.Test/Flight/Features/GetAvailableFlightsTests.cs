using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using EasyCaching.Core;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.CreatingFlight.V1;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using global::Flight.Flights.Models;

public class GetAvailableFlightsTests : FlightIntegrationTestBase
{
    public GetAvailableFlightsTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_return_available_flights()
    {
        // Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetAvailableFlights();
        await InvalidateCacheAsync(query);

        // Act
        var response = (await Fixture.SendAsync(query))?.FlightDtos?.ToList();

        // Assert
        response?.Should().NotBeNull();
        response?.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_exclude_deleted_flights_from_available_flights()
    {
        // Arrange
        var activeFlight = new FakeCreateFlightMongoCommand().Generate();
        var deletedFlight = new FakeCreateFlightMongoCommand().Generate() with { IsDeleted = true };

        await Fixture.SendAsync(activeFlight);
        await Fixture.SendAsync(deletedFlight);

        var query = new GetAvailableFlights();
        await InvalidateCacheAsync(query);

        // Act
        var response = (await Fixture.SendAsync(query))?.FlightDtos?.ToList();

        // Assert
        response.Should().NotBeNull();
        response.Should().Contain(x => x.Id == activeFlight.Id);
        response.Should().NotContain(x => x.Id == deletedFlight.Id);
    }

    [Fact]
    public async Task should_throw_flight_not_found_when_no_available_flights_exist()
    {
        // Arrange
        await Fixture.ExecuteReadContextAsync(db => db.Flight.DeleteManyAsync(Builders<FlightReadModel>.Filter.Empty));

        var query = new GetAvailableFlights();
        await InvalidateCacheAsync(query);

        // Act
        Func<Task> act = () => Fixture.SendAsync(query);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task should_throw_flight_not_found_when_only_deleted_flights_exist()
    {
        // Arrange
        await Fixture.ExecuteReadContextAsync(db => db.Flight.DeleteManyAsync(Builders<FlightReadModel>.Filter.Empty));

        var deletedFlight = new FakeCreateFlightMongoCommand().Generate() with { IsDeleted = true };
        await Fixture.SendAsync(deletedFlight);

        var query = new GetAvailableFlights();
        await InvalidateCacheAsync(query);

        // Act
        Func<Task> act = () => Fixture.SendAsync(query);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    private Task InvalidateCacheAsync(GetAvailableFlights query)
    {
        var cachingProvider = Fixture
            .ServiceProvider.GetRequiredService<IEasyCachingProviderFactory>()
            .GetCachingProvider("mem");

        return cachingProvider.RemoveAsync(query.CacheKey);
    }
}
