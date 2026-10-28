using System.Text.Json;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Flight;
using Flight.Data;
using Grpc.AspNetCore.HealthChecks;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace Flight.Host.Extensions;

public static class FlightHostInfrastructureExtensions
{
    private static readonly string[] LiveTag = ["live"];
    private static readonly string[] ReadyTag = ["ready"];

    public static WebApplicationBuilder AddFlightHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddCustomObservability();
        builder.Services.AddJwt();
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var metadata = builder.Configuration["Jwt:MetadataAddress"];
                if (!string.IsNullOrWhiteSpace(metadata))
                    options.MetadataAddress = metadata;
            }
        );

        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpEventHeadersProvider>();
        builder.Services.AddEventMapper<FlightEventMapper>();
        builder.Services.AddCustomMassTransit(
            builder.Environment,
            builder.Configuration,
            "flight-service",
            typeof(FlightRoot).Assembly
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcExceptionInterceptor>());
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Map("", context => context.Tags.Contains("live"));
            options.Services.Map("flight.FlightGrpcService", IsReadyCheck);
        });
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        AddHealthChecks(builder);

        return builder;
    }

    public static WebApplication UseFlightHostInfrastructure(this WebApplication app)
    {
        app.UseCustomObservability();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        var appName = app.Configuration["AppOptions:Name"] ?? "Flight Service";
        app.MapGet("/", () => appName);
        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions { Predicate = check => check.Tags.Contains("live"), ResponseWriter = WriteHealthCheckResponse }
        );
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live") || IsReadyCheck(check),
                ResponseWriter = WriteHealthCheckResponse,
            }
        );
        app.MapGrpcHealthChecksService();

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }

    private static bool IsReadyCheck(HealthCheckMapContext context) => IsReadyCheck(context.Name, context.Tags);

    private static bool IsReadyCheck(HealthCheckRegistration check) => IsReadyCheck(check.Name, check.Tags);

    private static bool IsReadyCheck(string name, IEnumerable<string> tags) =>
        tags.Contains("ready") && !string.Equals(name, "masstransit-bus", StringComparison.Ordinal);

    private static void AddHealthChecks(WebApplicationBuilder builder)
    {
        var healthChecks = builder.Services.AddHealthChecks().AddCheck(
            "self",
            () => HealthCheckResult.Healthy(),
            LiveTag
        );

        healthChecks.Add(
            new HealthCheckRegistration(
                "postgres-flight",
                serviceProvider =>
                    new NpgSqlHealthCheck(
                        new NpgSqlHealthCheckOptions(
                            serviceProvider.GetRequiredService<IConfiguration>().ResolvePostgresConnectionString("Flight")
                        )
                    ),
                HealthStatus.Unhealthy,
                ReadyTag
            )
        );
        healthChecks.Add(
            new HealthCheckRegistration(
                "postgres-flight-persist-message",
                serviceProvider =>
                    new NpgSqlHealthCheck(
                        new NpgSqlHealthCheckOptions(
                            serviceProvider
                                .GetRequiredService<IConfiguration>()
                                .ResolvePersistMessageConnectionString("Flight")
                        )
                    ),
                HealthStatus.Unhealthy,
                ReadyTag
            )
        );
        healthChecks.AddMongoDb(
            serviceProvider =>
                new MongoClient(serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Flight").ConnectionString),
            serviceProvider =>
                serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Flight").DatabaseName,
            name: "mongo-flight",
            failureStatus: HealthStatus.Unhealthy,
            tags: ReadyTag
        );

        if (UsesInMemoryTransport(builder))
            return;

        healthChecks.AddRabbitMQ(
            serviceProvider =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var connectionString = configuration.GetConnectionString("rabbitmq");
                var factory = new ConnectionFactory();
                if (!string.IsNullOrWhiteSpace(connectionString))
                    factory.Uri = new Uri(connectionString);
                else
                {
                    var options = configuration.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>() ?? new();
                    factory.HostName = options.HostName;
                    factory.Port = options.Port;
                    factory.VirtualHost = options.VirtualHost;
                    factory.UserName = options.UserName;
                    factory.Password = options.Password;
                }

                return factory.CreateConnectionAsync();
            },
            name: "rabbitmq",
            failureStatus: HealthStatus.Unhealthy,
            tags: ReadyTag
        );
    }

    private static bool UsesInMemoryTransport(WebApplicationBuilder builder)
    {
        var transport = builder.Configuration["Messaging:Transport"];
        return string.Equals(transport, "InMemory", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(transport) && builder.Environment.IsEnvironment("test"));
    }

    private static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(check => new
                {
                    name = check.Key,
                    status = check.Value.Status.ToString(),
                    description = check.Value.Description,
                }),
            }
        );
    }
}
