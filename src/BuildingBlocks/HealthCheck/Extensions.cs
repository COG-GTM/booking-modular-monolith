using BuildingBlocks.Web;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace BuildingBlocks.HealthCheck;

public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";
    public const string LivenessEndpointPath = "/health/live";
    public const string ReadinessEndpointPath = "/health/ready";
    public const string LiveTag = "live";

    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services)
    {
        var healthOptions = services.GetOptions<HealthOptions>(nameof(HealthOptions));

        if (healthOptions.Enabled)
        {
            var appOptions = services.GetOptions<AppOptions>(nameof(AppOptions));
            // Bound from the same sections as BuildingBlocks.EFCore/MassTransit/EventStoreDB/Mongo options,
            // so health checks don't need a reference to those libraries.
            var rabbitMqOptions = services.GetOptions<RabbitMqHealthOptions>("RabbitMqOptions");
            var rabbitMqUri = RabbitMqHealthUri.Resolve(
                services.GetOptions<RabbitMqConnectionStringOptions>("ConnectionStrings").RabbitMq,
                rabbitMqOptions.HostName,
                rabbitMqOptions.Port,
                rabbitMqOptions.VirtualHost,
                rabbitMqOptions.UserName,
                rabbitMqOptions.Password
            );
            var eventStoreOptions = services.GetOptions<ConnectionStringOptions>("EventStoreOptions");
            var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();

            var healthChecksBuilder = services
                .AddHealthChecks()
                .AddRabbitMQ(serviceProvider =>
                {
                    var factory = new ConnectionFactory { Uri = rabbitMqUri };
                    return factory.CreateConnectionAsync();
                });

            var mongoModules = configuration
                .GetSection("MongoOptions")
                .GetChildren()
                .Where(section => !string.IsNullOrEmpty(section["DatabaseName"]))
                .ToArray();
            foreach (var module in mongoModules)
            {
                var connection =
                    module["ConnectionString"]
                    ?? configuration.GetConnectionString("mongo")
                    ?? configuration["MongoOptions:ConnectionString"];
                if (!string.IsNullOrEmpty(connection))
                    healthChecksBuilder.AddMongoDb(
                        clientFactory: _ => new MongoClient(connection),
                        databaseNameFactory: _ => module["DatabaseName"],
                        name: $"MongoDB-{module.Key}",
                        failureStatus: HealthStatus.Unhealthy,
                        timeout: TimeSpan.FromSeconds(10)
                    );
            }
            if (mongoModules.Length == 0)
            {
                var connection =
                    configuration.GetConnectionString("mongo") ?? configuration["MongoOptions:ConnectionString"];
                if (!string.IsNullOrEmpty(connection))
                    healthChecksBuilder.AddMongoDb(
                        clientFactory: _ => new MongoClient(connection),
                        name: "MongoDB-Health"
                    );
            }

            foreach (var module in configuration.GetSection("PostgresOptions:ConnectionString").GetChildren())
            {
                var connection = configuration.GetConnectionString(module.Key.Kebaberize()) ?? module.Value;
                if (!string.IsNullOrEmpty(connection))
                    healthChecksBuilder.AddNpgSql(connection, name: $"Postgres-{module.Key}");
            }

            var postgresConnection = configuration["PostgresOptions:ConnectionString"];
            if (!string.IsNullOrEmpty(postgresConnection))
                healthChecksBuilder.AddNpgSql(postgresConnection);

            if (!string.IsNullOrEmpty(eventStoreOptions.ConnectionString))
                healthChecksBuilder.AddEventStore(eventStoreOptions.ConnectionString);

            if (healthOptions.UIEnabled)
            {
                services
                    .AddHealthChecksUI(setup =>
                    {
                        setup.SetEvaluationTimeInSeconds(60); // time in seconds between check
                        setup.AddHealthCheckEndpoint($"Self Check - {appOptions.Name}", HealthEndpointPath);
                    })
                    .AddInMemoryStorage();
            }
        }

        services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        if (healthOptions.MetricsEnabled)
            services.AddHealthCheckMetrics();

        return services;
    }

    public static WebApplication UseCustomHealthCheck(this WebApplication app)
    {
        var healthOptions = app.Configuration.GetOptions<HealthOptions>(nameof(HealthOptions));

        app.MapCustomHealthEndpoints();

        if (healthOptions.Enabled && healthOptions.UIEnabled)
            app.MapHealthChecksUI(options => options.UIPath = "/health-ui");

        return app;
    }

    // Probe contract shared by every host (all environments), consumed by compose healthchecks and K8s probes:
    // /health/live and /alive = "live" checks only; /health/ready = everything else; /health = all checks.
    public static IEndpointRouteBuilder MapCustomHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(
            HealthEndpointPath,
            new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync }
        );
        endpoints.MapHealthChecks(
            AlivenessEndpointPath,
            new HealthCheckOptions { Predicate = IsLivenessCheck, ResponseWriter = HealthResponseWriter.WriteAsync }
        );
        endpoints.MapHealthChecks(
            LivenessEndpointPath,
            new HealthCheckOptions { Predicate = IsLivenessCheck, ResponseWriter = HealthResponseWriter.WriteAsync }
        );
        endpoints.MapHealthChecks(
            ReadinessEndpointPath,
            new HealthCheckOptions { Predicate = IsReadinessCheck, ResponseWriter = HealthResponseWriter.WriteAsync }
        );

        return endpoints;
    }

    public static bool IsLivenessCheck(HealthCheckRegistration registration) =>
        registration.Tags.Contains(LiveTag);

    public static bool IsReadinessCheck(HealthCheckRegistration registration) =>
        !registration.Tags.Contains(LiveTag);
}
