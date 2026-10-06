using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Grpc;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Host.Extensions;

/// <summary>
/// Composes the standalone Booking service: shared web/messaging infrastructure, the Booking module
/// (endpoints, EventStoreDB write model + $all subscription, Mongo read model, own outbox, Flight/Passenger
/// gRPC clients) and readiness checks for the stores this service owns.
/// </summary>
public static class BookingHostExtensions
{
    public static WebApplicationBuilder AddBookingHost(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpContextEventHeadersProvider>();

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            TransportType.RabbitMq,
            typeof(BookingRoot).Assembly
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });
        builder.Services.AddGrpcHealthService();

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });
        builder.Services.AddProblemDetails();

        builder.AddBookingModules();
        builder.AddBookingStoreHealthChecks();

        return builder;
    }

    public static WebApplication UseBookingHost(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseBookingModules();

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));
        app.MapGrpcHealthService();

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        app.MapMinimalEndpoints();

        return app;
    }
}
