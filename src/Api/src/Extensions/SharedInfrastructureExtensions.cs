using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Grpc.AspNetCore.HealthChecks;
using Figgle.Fonts;
using Flight;
using Identity;
using Microsoft.AspNetCore.Mvc;
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
        builder.AddPersistMessageProcessor();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddEventDispatcher();
        builder.Services.AddHttpContextEventHeaders();

        builder.Services.AddCustomMassTransit(
            builder.Environment,
            TransportType.InMemory,
            AppDomain.CurrentDomain.GetAssemblies()
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });

        // grpc.health.v1 for the gRPC services this host serves; outbound gRPC dependency checks are
        // excluded so the host never reports on itself.
        builder.Services.AddGrpcHealthChecks(options =>
        {
            Func<HealthCheckMapContext, bool> excludeGrpcDependencies = context =>
                !context.Tags.Contains(GrpcClientExtensions.GrpcDependencyTag);

            options.Services.Map(string.Empty, excludeGrpcDependencies);
            options.Services.Map(FlightGrpcService.Descriptor.FullName, excludeGrpcDependencies);
            options.Services.Map(PassengerGrpcService.Descriptor.FullName, excludeGrpcDependencies);
        });

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

        app.UseServiceDefaults();

        app.UseCustomProblemDetails();

        app.UseCorrelationId();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));
        app.MapGrpcHealthChecksService();

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}