using System.Net;
using System.Text.Json;
using BuildingBlocks.ProblemDetails;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Test;

public class ConcurrencyProblemDetailsTests
{
    [Theory]
    [InlineData("concurrency", HttpStatusCode.Conflict)]
    [InlineData("derived", HttpStatusCode.Conflict)]
    [InlineData("database", HttpStatusCode.InternalServerError)]
    [InlineData("same-short-name", HttpStatusCode.InternalServerError)]
    public async Task split_middleware_preserves_concurrency_detection_without_matching_unrelated_exceptions(
        string exceptionKind,
        HttpStatusCode expectedStatus
    )
    {
        Exception exception = exceptionKind switch
        {
            "concurrency" => new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Concurrent update"),
            "derived" => new DerivedConcurrencyException("Concurrent update"),
            "database" => new DbUpdateException("Database update failed"),
            _ => new DbUpdateConcurrencyException("Not an EF exception"),
        };
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseCustomProblemDetails();
        app.Run(_ => throw exception);
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/failure");

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("status").GetInt32().Should().Be((int)expectedStatus);
        problem.RootElement.GetProperty("title").GetString().Should().Be(exception.GetType().Name);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(exception.Message);
        problem.RootElement.TryGetProperty("exception", out _).Should().BeFalse();
    }

    private sealed class DerivedConcurrencyException(string message)
        : Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(message);

    private sealed class DbUpdateConcurrencyException(string message) : Exception(message);
}
