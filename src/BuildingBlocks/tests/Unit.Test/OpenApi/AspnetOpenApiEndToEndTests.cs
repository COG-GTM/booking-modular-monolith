using System.Text.Json;
using BuildingBlocks.OpenApi;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.OpenApiTests;

public class AspnetOpenApiEndToEndTests
{
    [Fact]
    public async Task openapi_document_exposes_bearer_and_api_key_and_scalar_is_served()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddCustomVersioning();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        var app = builder.Build();
        app.MapGet("/bb-openapi-ping", () => "pong");
        app.UseAspnetOpenApi();
        await app.StartAsync();
        await using (app)
        {
            var client = app.GetTestClient();

            var docResponse = await client.GetAsync("/openapi/v1.json");

            docResponse.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await docResponse.Content.ReadAsStringAsync());
            var schemes = doc.RootElement.GetProperty("components").GetProperty("securitySchemes");
            schemes.TryGetProperty("Bearer", out _).Should().BeTrue();
            schemes.TryGetProperty("ApiKey", out _).Should().BeTrue();

            var scalarResponse = await client.GetAsync("/scalar/v1");

            scalarResponse.EnsureSuccessStatusCode();
        }
    }
}
