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
using Figgle.Fonts;
using Flight;
using Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        builder.Services.AddHttpContextAccessor();
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

        // Standard grpc.health.v1 service for the Flight/Passenger servers. Checks that probe *other* gRPC services are
        // excluded so a service's status reflects only its own dependencies (and the monolith never probes itself in a loop).
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Clear();
            options.Services.Map(string.Empty, IsOwnDependency);
            options.Services.Map(FlightGrpcService.Descriptor.FullName, IsOwnDependency);
            options.Services.Map(PassengerGrpcService.Descriptor.FullName, IsOwnDependency);
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

        app.MapGrpcHealthChecksService();

        // Readiness in every environment: every check except liveness-only ones, i.e. the Flight/Passenger gRPC dependencies plus
        // Postgres/Mongo/RabbitMQ/EventStoreDB when HealthOptions:Enabled. /health and /alive stay Development-only.
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = IsReadinessCheck,
        });

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }

    public const string ReadinessEndpointPath = "/health/ready";

    public static bool IsReadinessCheck(HealthCheckRegistration registration) =>
        !registration.Tags.Contains(LivenessHealthTag);

    private const string LivenessHealthTag = "live";

    private static bool IsOwnDependency(Grpc.AspNetCore.HealthChecks.HealthCheckMapContext context) =>
        !context.Tags.Contains(GrpcClientExtensions.GrpcDependencyHealthTag);
}
