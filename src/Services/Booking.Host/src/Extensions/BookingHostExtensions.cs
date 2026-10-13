using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;

namespace Booking.Host.Extensions;

// Cross-cutting infrastructure for the standalone Booking service: the single-module counterpart of the
// monolith's SharedInfrastructureExtensions, composed from the focused BuildingBlocks projects only.
public static class BookingHostExtensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";
    public const string BookingModuleName = "Booking";

    public static WebApplicationBuilder AddBookingHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddBookingServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();

        // Booking's own outbox/inbox store (booking_service_persist_message).
        builder.AddPersistMessageProcessor<BookingRoot>(BookingModuleName);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddEventDispatcher();
        builder.Services.AddHttpContextEventHeaders();

        // Transport, queue prefix and inbox come from the "MessageBroker" section; only Booking's consumers.
        builder.Services.AddCustomMassTransit(builder.Configuration, builder.Environment, typeof(BookingRoot).Assembly);

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });
        builder.Services.AddProblemDetails();

        return builder;
    }

    // Health checks, OpenTelemetry, service discovery and resilient HttpClient defaults (the Aspire
    // ServiceDefaults set, without pulling the aggregate BuildingBlocks project into this host).
    public static WebApplicationBuilder AddBookingServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.AddCustomHealthCheck();
        builder.Services.AddBookingReadinessChecks(builder.Configuration);
        builder.AddCustomObservability();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static WebApplication UseBookingHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseCustomHealthCheck();
        app.UseCustomObservability();

        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
