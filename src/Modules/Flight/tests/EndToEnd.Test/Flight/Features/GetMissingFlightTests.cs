using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

using global::Flight.Flights.Exceptions;

public class GetMissingFlightTests : FlightEndToEndTestBase
{
    public GetMissingFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task should_return_not_found_for_an_unknown_flight_id()
    {
        var route = ApiRoutes.Flight.GetFlightById.Replace(
            ApiRoutes.Flight.Id,
            Guid.NewGuid().ToString(),
            StringComparison.Ordinal
        );

        using var client = Fixture.HttpClient;
        using var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be(nameof(FlightNotFountException));
    }
}
