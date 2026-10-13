using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Exception;
using BuildingBlocks.ProblemDetails;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight.Seats.Exceptions;

public class CustomProblemDetailsTests
{
    [Fact]
    public async Task seat_already_reserved_exception_should_be_mapped_to_409_conflict()
    {
        var (statusCode, problem) = await SendRequestThrowing(new SeatAlreadyReservedException());

        statusCode.Should().Be(HttpStatusCode.Conflict);
        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Title.Should().Be(nameof(SeatAlreadyReservedException));
        problem.Detail.Should().Be("Seat is already reserved!");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, StatusCodes.Status400BadRequest)]
    [InlineData(HttpStatusCode.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity, StatusCodes.Status422UnprocessableEntity)]
    public async Task app_exception_should_be_mapped_to_its_own_status_code(
        HttpStatusCode exceptionStatus,
        int expected
    )
    {
        var (statusCode, problem) = await SendRequestThrowing(new AppException("boom", exceptionStatus));

        ((int)statusCode).Should().Be(expected);
        problem.Status.Should().Be(expected);
        problem.Title.Should().Be(nameof(AppException));
        problem.Detail.Should().Be("boom");
    }

    [Fact]
    public async Task db_update_concurrency_exception_should_be_mapped_to_409_conflict()
    {
        var (statusCode, problem) = await SendRequestThrowing(new DbUpdateConcurrencyException("conflict"));

        statusCode.Should().Be(HttpStatusCode.Conflict);
        problem.Status.Should().Be(StatusCodes.Status409Conflict);
        problem.Title.Should().Be(nameof(DbUpdateConcurrencyException));
    }

    private static async Task<(HttpStatusCode StatusCode, ProblemDetails Problem)> SendRequestThrowing(
        System.Exception exception
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();

        await using var app = builder.Build();
        app.UseCustomProblemDetails();
        RequestDelegate throwingEndpoint = _ => throw exception;
        app.MapGet("/throw", throwingEndpoint);
        await app.StartAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/throw");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        await app.StopAsync();

        return (response.StatusCode, problem!);
    }
}
