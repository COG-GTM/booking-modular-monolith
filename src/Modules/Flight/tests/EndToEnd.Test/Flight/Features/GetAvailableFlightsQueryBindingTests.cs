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

    [Fact]
    public async Task should_bind_defaults_and_return_distinct_requested_pages()
    {
        var flights = Enumerable.Range(0, 30).Select(index => NewFlight(index)).ToList();
        await Fixture.ExecuteReadContextAsync(db => db.Flight.InsertManyAsync(flights));
        using var client = Fixture.HttpClient;

        using var defaultResponse = await client.GetAsync(Route);
        using var firstResponse = await client.GetAsync(Route + "?pageNumber=1&pageSize=5");
        using var secondResponse = await client.GetAsync(Route + "?pageNumber=2&pageSize=5");

        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var defaultPage = await defaultResponse.Content.ReadFromJsonAsync<GetAvailableFlightsResponseDto>();
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<GetAvailableFlightsResponseDto>();
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<GetAvailableFlightsResponseDto>();
        defaultPage.Should().NotBeNull();
        firstPage.Should().NotBeNull();
        secondPage.Should().NotBeNull();
        defaultPage!.FlightDtos.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(20);
        firstPage!.FlightDtos.Should().HaveCount(5);
        secondPage!.FlightDtos.Should().HaveCount(5);
        firstPage.FlightDtos.Select(x => x.Id).Should().NotIntersectWith(secondPage.FlightDtos.Select(x => x.Id));
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
