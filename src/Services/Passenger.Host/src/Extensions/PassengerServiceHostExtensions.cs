using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Contracts.Grpc.Passenger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PassengerRoot = Passenger.PassengerRoot;

namespace Passenger.Host.Extensions;

public static class PassengerServiceHostExtensions
{
    public const string ReadyTag = "ready";
    public const string LivenessEndpointPath = "/health/live";
    public const string ReadinessEndpointPath = "/health/ready";

    // Shared infrastructure for the standalone Passenger service; module DI comes from AddPassengerModules.
    public static WebApplicationBuilder AddPassengerServiceHost(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddEventDispatcher();
        builder.Services.AddHttpContextEventHeaders();

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            typeof(PassengerRoot).Assembly
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Map(string.Empty, _ => true);
            options.Services.Map(PassengerGrpcService.Descriptor.FullName, _ => true);
        });

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });

        builder.Services.AddProblemDetails();

        builder.AddPassengerHealthChecks();

        return builder;
    }

    public static WebApplication UsePassengerServiceHost(this WebApplication app)
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

    // Readiness covers the service-owned stores (MassTransit adds its own "ready" bus check). Connection
    // strings resolve at check time so configuration added after the builder (tests, Aspire) is honoured.
    private static void AddPassengerHealthChecks(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddHealthChecks()
            .AddNpgSql(
                sp => PassengerConnectionString(sp.GetRequiredService<IConfiguration>()),
                name: "passenger-postgres",
                tags: [ReadyTag]
            )
            .AddNpgSql(
                sp =>
                    BuildingBlocks.PersistMessageProcessor.Extensions.GetPersistMessageConnectionString(
                        sp.GetRequiredService<IConfiguration>(),
                        nameof(Passenger)
                    ),
                name: "passenger-persist-message-postgres",
                tags: [ReadyTag]
            )
            .AddMongoDb(
                clientFactory: sp => new MongoClient(
                    sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Passenger)).ConnectionString
                ),
                name: "passenger-mongo",
                tags: [ReadyTag]
            );
    }

    private static string PassengerConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(nameof(Passenger).ToLowerInvariant())
        ?? configuration[$"{nameof(PostgresOptions)}:ConnectionString:{nameof(Passenger)}"]
        ?? throw new InvalidOperationException("Passenger Postgres connection string is not configured.");
}
