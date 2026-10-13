using System.Net;
using System.Net.Http.Json;
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

namespace Unit.Test.Common;

public class ProblemDetailsForbiddenExceptionTests
{
    private sealed class DerivedForbiddenException : ForbiddenException
    {
        public DerivedForbiddenException()
            : base("You are not allowed to modify this resource!") { }
    }

    [Fact]
    public void forbidden_exception_should_carry_403_status_code()
    {
        var exception = new ForbiddenException("denied", code: 7);

        exception.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exception.Message.Should().Be("denied");
        exception.Code.Should().Be(7);
        exception.Should().BeAssignableTo<CustomException>();
    }

    [Theory]
    [InlineData(typeof(ForbiddenException))]
    [InlineData(typeof(DerivedForbiddenException))]
    public async Task custom_problem_details_should_map_forbidden_exception_to_403(Type exceptionType)
    {
        await using var app = await CreateAppAsync(exceptionType);
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/throw");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Title.Should().Be(exceptionType.Name);
        problem.Detail.Should().Be("You are not allowed to modify this resource!");
    }

    private static async Task<WebApplication> CreateAppAsync(Type exceptionType)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseCustomProblemDetails();
        app.MapGet(
            "/throw",
            () =>
            {
                throw exceptionType == typeof(ForbiddenException)
                    ? new ForbiddenException("You are not allowed to modify this resource!")
                    : new DerivedForbiddenException();
            }
        );

        await app.StartAsync();

        return app;
    }
}
