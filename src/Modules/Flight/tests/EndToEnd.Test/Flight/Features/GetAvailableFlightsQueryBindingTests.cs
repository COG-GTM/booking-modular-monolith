using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Flights.Enums;
using Flight.Flights.Features.GettingAvailableFlights.V1;
using Flight.Flights.Models;
using FluentAssertions;
using MassTransit;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class GetAvailableFlightsQueryBindingTests : FlightEndToEndTestBase
{
    private const string Route = "/api/v1/flight/get-available-flights";

    public GetAvailableFlightsQueryBindingTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData("?pageNumber=1&pageSize=101")]
    [InlineData("?pageNumber=0")]
    public async Task should_reject_invalid_paging_bound_from_query_string(string query)
    {
        using var client = Fixture.HttpClient;

        using var response = await client.GetAsync(Route + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // One request per case: the shared test fixture's singleton IHttpContextAccessor mock
    // is not safe for back-to-back requests within a single test.
    [Theory]
    [InlineData("", GetAvailableFlights.DefaultPageSize)]
    [InlineData("?pageNumber=2&pageSize=5", 5)]
    public async Task should_bind_paging_from_query_string_and_bound_page_size(string query, int expectedMaxCount)
    {
        var flights = Enumerable.Range(0, 30).Select(index => NewFlight(index)).ToList();
        await Fixture.ExecuteReadContextAsync(db => db.Flight.InsertManyAsync(flights));
        using var client = Fixture.HttpClient;

        using var response = await client.GetAsync(Route + query);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<GetAvailableFlightsResponseDto>();
        page.Should().NotBeNull();
        page!.FlightDtos.Should().HaveCount(expectedMaxCount);
    }

    private static FlightReadModel NewFlight(int index)
    {
        var id = NewId.NextGuid();
        return new FlightReadModel
        {
            Id = NewId.NextGuid(),
            FlightId = id,
            FlightNumber = "HTTP" + index,
            AircraftId = Guid.NewGuid(),
            DepartureDate = DateTime.UtcNow,
            DepartureAirportId = Guid.NewGuid(),
            ArriveDate = DateTime.UtcNow.AddHours(1),
            ArriveAirportId = Guid.NewGuid(),
            DurationMinutes = 60,
            FlightDate = DateTime.UtcNow.AddDays(index),
            Status = FlightStatus.Flying,
            Price = 100,
            IsDeleted = false,
        };
    }
}
