using Booking;
using BuildingBlocks.Core;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Host.Extensions;

public static class BookingHostInfrastructureExtensions
{
    public static WebApplicationBuilder AddBookingHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddCustomObservability();
        builder.Services.AddJwt();
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var metadataAddress = builder.Configuration["Jwt:MetadataAddress"];
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                    options.MetadataAddress = metadataAddress;
            }
        );
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.AddPersistMessageProcessor<BookingRoot>("Booking");

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpEventHeadersProvider>();
        builder.Services.AddCustomMassTransit(
            builder.Environment,
            builder.Configuration,
            "booking-service",
            typeof(BookingRoot).Assembly
        );
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        builder.Services.AddEventMapper<BookingEventMapper>();
        builder.Services.AddBookingHealthChecks(builder.Configuration, builder.Environment);

        return builder;
    }

    public static WebApplication UseBookingHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));
        app.UseCustomObservability();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapGet("/", () => appOptions.Name);
        app.MapBookingHealthChecks();
        if (app.Environment.IsDevelopment())
            app.UseAspnetOpenApi();

        return app;
    }
}
