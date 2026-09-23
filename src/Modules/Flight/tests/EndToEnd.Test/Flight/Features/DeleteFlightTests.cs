using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;

public class DeleteFlightTests : FlightEndToEndTestBase
{
    public DeleteFlightTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_delete_existing_flight_and_return_no_content()
    {
        // Arrange
        var command = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(command);

        // Act
        var route = ApiRoutes.Flight.DeleteFlight.Replace(
            ApiRoutes.Flight.Id,
            command.Id.ToString(),
            StringComparison.CurrentCulture
        );
        var result = await Fixture.HttpClient.DeleteAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deletedFlight = await Fixture.ExecuteDbContextAsync(db =>
            db.Flights.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == FlightId.Of(command.Id))
        );

        deletedFlight.Should().NotBeNull();
        deletedFlight!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_flight_not_found_problem_details_when_flight_does_not_exist()
    {
        // Arrange
        var missingId = NewId.NextGuid();

        // Act
        var route = ApiRoutes.Flight.DeleteFlight.Replace(
            ApiRoutes.Flight.Id,
            missingId.ToString(),
            StringComparison.CurrentCulture
        );
        var result = await Fixture.HttpClient.DeleteAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be(nameof(FlightNotFountException));
        problem.Detail.Should().Be(new FlightNotFountException().Message);
        problem.Status.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_return_unauthorized_when_request_has_no_token()
    {
        // Arrange
        var command = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(command);

        var httpClient = Fixture.HttpClient;
        httpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        var route = ApiRoutes.Flight.DeleteFlight.Replace(
            ApiRoutes.Flight.Id,
            command.Id.ToString(),
            StringComparison.CurrentCulture
        );
        var result = await httpClient.DeleteAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var flight = await Fixture.ExecuteDbContextAsync(db =>
            db.Flights.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == FlightId.Of(command.Id))
        );

        flight.Should().NotBeNull();
        flight!.IsDeleted.Should().BeFalse();
    }
}
