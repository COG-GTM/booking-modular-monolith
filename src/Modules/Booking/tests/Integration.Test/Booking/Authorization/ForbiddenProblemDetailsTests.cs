using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Booking.Booking.Exceptions;
using BuildingBlocks.Exception;
using BuildingBlocks.ProblemDetails;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Integration.Test.Booking.Authorization;

public class ForbiddenProblemDetailsTests
{
    [Fact]
    public async Task passenger_booking_forbidden_exception_is_returned_as_403_problem_details()
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/passenger-forbidden");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<MvcProblemDetails>();
        problem!.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Title.Should().Be(nameof(PassengerBookingForbiddenException));
        problem.Detail.Should().Be("You are not allowed to create a booking for this passenger.");
    }

    [Fact]
    public async Task base_forbidden_exception_is_returned_as_403()
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/forbidden");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<MvcProblemDetails>();
        problem!.Title.Should().Be(nameof(ForbiddenException));
        problem.Detail.Should().Be("nope");
    }

    [Fact]
    public async Task passenger_not_found_exception_is_returned_as_404_problem_details()
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/passenger-not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<MvcProblemDetails>();
        problem!.Status.Should().Be(StatusCodes.Status404NotFound);
        problem.Title.Should().Be(nameof(PassengerNotFoundException));
        problem.Detail.Should().Be("Passenger not found!");
    }

    private static async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseCustomProblemDetails();
        app.MapGet("/passenger-forbidden", IResult () => throw new PassengerBookingForbiddenException());
        app.MapGet("/forbidden", IResult () => throw new ForbiddenException("nope"));
        app.MapGet("/passenger-not-found", IResult () => throw new PassengerNotFoundException());

        await app.StartAsync();
        return app;
    }
}
