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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Booking.Features;

public class ForbiddenProblemDetailsTests
{
    [Theory]
    [InlineData(false, nameof(ForbiddenException), "Access denied")]
    [InlineData(true, nameof(PassengerAccessDeniedException), "You are not allowed to book for this passenger!")]
    public async Task should_map_forbidden_exceptions_to_403_problem_details(
        bool passengerDenied,
        string title,
        string detail
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseCustomProblemDetails();
        app.MapGet(
            "/denied",
            (HttpContext _) =>
            {
                throw passengerDenied ? new PassengerAccessDeniedException() : new ForbiddenException("Access denied");
            }
        );
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/denied");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(403);
        problem.Title.Should().Be(title);
        problem.Detail.Should().Be(detail);
        problem.Extensions.Should().NotContainKey("exception");
    }
}
