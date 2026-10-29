using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Flight;
using Flight.GrpcServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Flight.Host.Extensions;

public static class FlightHostInfrastructureExtensions
{
    public static WebApplicationBuilder AddFlightHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddEventDispatcher();
        builder.Services.AddHttpContextEventHeaders();
        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            configureBus: null,
            typeof(FlightRoot).Assembly
        );
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcExceptionInterceptor>());
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Clear();
            options.Services.Map(string.Empty, IsReadinessCheck);
            options.Services.Map(FlightGrpcService.Descriptor.FullName, IsReadinessCheck);
        });
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        builder.Services.AddFlightReadinessChecks();

        return builder;
    }

    public static WebApplication UseFlightHostInfrastructure(this WebApplication app)
    {
        var appName = app.Configuration["AppOptions:Name"] ?? "Flight-Service";

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapGrpcHealthChecksService();
        app.MapGet("/", () => appName);

        if (app.Environment.IsDevelopment())
            app.UseAspnetOpenApi();

        return app;
    }

    // The probe endpoints themselves come from ServiceDefaults (UseCustomHealthCheck); kept for host tests.
    public static WebApplication MapFlightHealthEndpoints(this WebApplication app)
    {
        app.MapCustomHealthEndpoints();
        return app;
    }

    private static bool IsReadinessCheck(Grpc.AspNetCore.HealthChecks.HealthCheckMapContext context) =>
        !context.Tags.Contains("live");
}
