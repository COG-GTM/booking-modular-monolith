using System.Net;
using System.Text.Json;
using BuildingBlocks.Exception;
using BuildingBlocks.ProblemDetails;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.ProblemDetailsTests;

public class CustomProblemDetailsTests
{
    private static WebApplication BuildApp(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseCustomProblemDetails();
        app.MapGet("/bb-throw/{kind}", (string kind) => ThrowFor(kind));
        return app;
    }

    private static object ThrowFor(string kind) => throw Create(kind);

    private static System.Exception Create(string kind) =>
        kind switch
        {
            "conflict" => new ConflictException("already exists"),
            "validation" => new ValidationException("name is required"),
            "badrequest" => new BadRequestException("bad input"),
            "notfound" => new NotFoundException("missing"),
            "app" => new AppException("app failed"),
            "concurrency" => new DbUpdateConcurrencyException("row conflict"),
            "rpc" => new RpcException(new Status(StatusCode.NotFound, "rpc went missing")),
            _ => new InvalidOperationException("unexpected"),
        };

    [Theory]
    [InlineData("conflict", "ConflictException", "already exists", 409)]
    [InlineData("validation", "ValidationException", "name is required", 400)]
    [InlineData("badrequest", "BadRequestException", "bad input", 400)]
    [InlineData("notfound", "NotFoundException", "missing", 404)]
    [InlineData("app", "AppException", "app failed", 400)]
    [InlineData("concurrency", "DbUpdateConcurrencyException", "row conflict", 409)]
    [InlineData("rpc", "RpcException", "Status(StatusCode=\"NotFound\", Detail=\"rpc went missing\")", 400)]
    [InlineData("unexpected", "InvalidOperationException", "unexpected", 500)]
    public async Task thrown_exception_is_mapped_to_problem_details(
        string kind,
        string expectedTitle,
        string expectedDetail,
        int expectedStatus
    )
    {
        await using var app = BuildApp("Production");
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/bb-throw/{kind}");

        response.StatusCode.Should().Be((HttpStatusCode)expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
        doc.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(expectedStatus);
    }

    [Fact]
    public async Task development_responses_include_the_exception_tostring_extension()
    {
        await using var app = BuildApp("Development");
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/bb-throw/notfound");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("exception")
            .GetString()
            .Should()
            .Contain(nameof(NotFoundException))
            .And.Contain("missing");
    }

    [Fact]
    public async Task production_responses_omit_the_exception_extension()
    {
        await using var app = BuildApp("Production");
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/bb-throw/notfound");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("exception", out _).Should().BeFalse();
    }

    [Fact]
    public async Task unknown_route_returns_404_problem_details_via_status_code_pages()
    {
        await using var app = BuildApp("Production");
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/bb-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Not Found");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(404);
    }
}
