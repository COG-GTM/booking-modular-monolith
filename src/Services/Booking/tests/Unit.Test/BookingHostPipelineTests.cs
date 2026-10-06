using Booking.Host.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Booking.Host.Unit.Test;

public class BookingHostPipelineTests
{
    private static WebApplication BuildApp(string environment = "test")
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = environment,
            }
        );

        builder.AddBookingHost();

        var app = builder.Build();
        app.UseBookingHost();

        return app;
    }

    private static List<RouteEndpoint> GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();

    [Fact]
    public async Task root_endpoint_returns_configured_service_name()
    {
        using var app = BuildApp();
        var root = GetRouteEndpoints(app).Single(e => e.RoutePattern.RawText == "/");

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();

        await root.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Be(app.Configuration["AppOptions:Name"]).And.Be("Booking-Service");
    }

    [Fact]
    public void maps_grpc_health_service_for_aspire_and_sidecar_probes()
    {
        using var app = BuildApp();

        GetRouteEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .Should()
            .Contain(["/grpc.health.v1.Health/Check", "/grpc.health.v1.Health/Watch"]);
    }

    [Fact]
    public void exposes_openapi_document_only_in_development()
    {
        using var development = BuildApp(Environments.Development);
        using var production = BuildApp(Environments.Production);

        GetRouteEndpoints(development).Should().Contain(e => e.RoutePattern.RawText!.StartsWith("/openapi/"));
        GetRouteEndpoints(production).Should().NotContain(e => e.RoutePattern.RawText!.StartsWith("/openapi/"));
    }

    [Fact]
    public void maps_versioned_booking_endpoints_only_once()
    {
        using var app = BuildApp();

        var bookingRoutes = GetRouteEndpoints(app)
            .Where(e => e.RoutePattern.RawText == "api/v{version:apiVersion}/booking")
            .Select(e => string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []))
            .ToList();

        bookingRoutes.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
    }
}
