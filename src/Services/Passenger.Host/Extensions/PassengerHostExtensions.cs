using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using BuildingBlocks.Grpc;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Passenger.Host.Extensions;

public static class PassengerHostExtensions
{
    public static WebApplicationBuilder AddPassengerHost(this WebApplicationBuilder builder)
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
        builder.Services.AddEventDispatcher();

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            assembly:
            [
                typeof(PassengerRoot).Assembly,
            ]
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

        // Host-local readiness checks for the Passenger module's own stores; RabbitMQ
        // is covered by MassTransit's bus health check.
        builder.Services
            .AddHealthChecks()
            .AddNpgSql(
                sp =>
                    sp.GetRequiredService<IConfiguration>()
                        .GetPostgresConnectionString(nameof(Passenger)),
                name: "Passenger-Postgres-Health",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10)
            )
            .AddMongoDb(
                sp =>
                    new MongoClient(
                        sp.GetRequiredService<IOptionsMonitor<MongoOptions>>()
                            .Get(nameof(Passenger))
                            .ConnectionString
                    ),
                name: "Passenger-MongoDB-Health",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10)
            );

        return builder;
    }

    public static WebApplication UsePassengerHost(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

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
