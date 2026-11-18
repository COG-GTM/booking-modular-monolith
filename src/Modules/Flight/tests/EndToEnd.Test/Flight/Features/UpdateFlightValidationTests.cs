using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Flights.Enums;
using Flight.Flights.Features.UpdatingFlight.V1;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class UpdateFlightValidationTests : FlightEndToEndTestBase
{
    public UpdateFlightValidationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData("Price", "Price must be greater than 0")]
    [InlineData("Status", "Status must be Flying, Delay, Canceled or Completed")]
    [InlineData("Id", "Id must be not empty")]
    public async Task should_return_bad_request_problem_details_when_update_flight_is_invalid(
        string field,
        string message
    )
    {
        var request = new UpdateFlightRequestDto(
            Guid.NewGuid(),
            "12FF",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(1),
            Guid.NewGuid(),
            60,
            DateTime.UtcNow,
            FlightStatus.Flying,
            800,
            false
        );
        request = field switch
        {
            "Price" => request with { Price = 0 },
            "Status" => request with { Status = FlightStatus.Unknown },
            "Id" => request with { Id = Guid.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        using var client = Fixture.HttpClient;
        using var response = await client.PutAsJsonAsync("/api/v1/flight", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(400);
        problem.Title.Should().Be("ValidationException");
        problem.Detail.Should().Be(message);
    }
}
