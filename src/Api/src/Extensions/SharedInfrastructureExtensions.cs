using Booking;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Exception;
using BuildingBlocks.Grpc;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Flight;
using Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Passenger;

namespace Api.Extensions;

public static class SharedInfrastructureExtensions
{
    public static WebApplicationBuilder AddSharedInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpContextEventHeadersProvider>();

        var moduleConsumerAssemblies = builder.Configuration.WhereModuleBackgroundProcessingEnabled(
        [
            (nameof(Flight), typeof(FlightEventMapper).Assembly),
            (nameof(Identity), typeof(IdentityEventMapper).Assembly),
            (nameof(Passenger), typeof(PassengerEventMapper).Assembly),
            (nameof(Booking), typeof(BookingEventMapper).Assembly),
        ]);

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            assembly: moduleConsumerAssemblies.ToArray()
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

        return builder;
    }

    public static WebApplication UserSharedInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        foreach (var module in new[] { nameof(Flight), nameof(Identity), nameof(Passenger), nameof(Booking) })
        {
            if (!app.Configuration.IsModuleBackgroundProcessingEnabled(module))
            {
                app.Logger.LogWarning(
                    "Background processing for module {Module} is disabled (Modules:{Module}:BackgroundProcessingEnabled=false); its standalone host is expected to process its outbox/projections/consumers.",
                    module
                );
            }
        }

        app.UseServiceDefaults();

        app.UseCustomProblemDetails();

        app.UseCorrelationId();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));
        app.MapGrpcHealthService();

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
