using Booking;
using BuildingBlocks.Core;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using EasyCaching.Core;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Booking.Host.Extensions;

public static class HostInfrastructureExtensions
{
    public static WebApplicationBuilder AddBookingHostInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(appOptions.Name);

        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.AddPersistMessageProcessor<BookingRoot>("Booking");

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
            ModuleBackgroundProcessing.ConsumerAssemblies(
                builder.Configuration,
                ("Booking", typeof(BookingRoot).Assembly)
            )
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        builder.Services.AddBookingHostHealthChecks(builder.Configuration);

        return builder;
    }

    public static WebApplication UseBookingHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live"),
                ResponseWriter = WriteHealthResponseAsync,
            }
        );
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = IsReadinessCheck, ResponseWriter = WriteHealthResponseAsync }
        );
        app.MapGet("/", () => appOptions.Name);

        if (app.Environment.IsDevelopment())
            app.UseAspnetOpenApi();

        return app;
    }

    private static bool IsReadinessCheck(HealthCheckRegistration registration) => !registration.Tags.Contains("live");

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }
                ),
            }
        );
}
