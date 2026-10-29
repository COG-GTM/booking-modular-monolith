using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace BuildingBlocks.HealthCheck;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services)
    {
        var healthOptions = services.GetOptions<HealthOptions>(nameof(HealthOptions));

        if (healthOptions.Enabled)
        {
            var appOptions = services.GetOptions<AppOptions>(nameof(AppOptions));
            // Bound from the same sections as BuildingBlocks.EFCore/MassTransit/EventStoreDB/Mongo options,
            // so health checks don't need a reference to those libraries.
            var postgresOptions = services.GetOptions<ConnectionStringOptions>("PostgresOptions");
            var rabbitMqOptions = services.GetOptions<RabbitMqHealthOptions>("RabbitMqOptions");
            var rabbitMqUri = RabbitMqHealthUri.Resolve(
                services.GetOptions<RabbitMqConnectionStringOptions>("ConnectionStrings").RabbitMq,
                rabbitMqOptions.HostName,
                rabbitMqOptions.Port,
                rabbitMqOptions.VirtualHost,
                rabbitMqOptions.UserName,
                rabbitMqOptions.Password);
            var eventStoreOptions = services.GetOptions<ConnectionStringOptions>("EventStoreOptions");
            var mongoOptions = services.GetOptions<ConnectionStringOptions>("MongoOptions");

            var healthChecksBuilder = services.AddHealthChecks()
                // Add a default liveness check to ensure app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
                .AddRabbitMQ(
                    serviceProvider =>
                    {
                        var factory = new ConnectionFactory
                        {
                            Uri = rabbitMqUri,
                        };
                        return factory.CreateConnectionAsync();
                    });

            if (!string.IsNullOrEmpty(mongoOptions.ConnectionString))
            {
                healthChecksBuilder.AddMongoDb(
                    clientFactory: _ => new MongoClient(mongoOptions.ConnectionString),
                    name: "MongoDB-Health",
                    failureStatus: HealthStatus.Unhealthy,
                    timeout: TimeSpan.FromSeconds(10));
            }

            if (!string.IsNullOrEmpty(postgresOptions.ConnectionString))
                healthChecksBuilder.AddNpgSql(postgresOptions.ConnectionString);

            if (!string.IsNullOrEmpty(eventStoreOptions.ConnectionString))
                healthChecksBuilder.AddEventStore(eventStoreOptions.ConnectionString);

            services.AddHealthChecksUI(setup =>
                                       {
                                           setup.SetEvaluationTimeInSeconds(60); // time in seconds between check
                                           setup.AddHealthCheckEndpoint($"Self Check - {appOptions.Name}", HealthEndpointPath);
                                       }).AddInMemoryStorage();
        }

        services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return services;
    }

    public static WebApplication UseCustomHealthCheck(this WebApplication app)
    {
        var healthOptions = app.Configuration.GetOptions<HealthOptions>(nameof(HealthOptions));

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            });
        }

        if (healthOptions.Enabled)
            app.MapHealthChecksUI(options => options.UIPath = "/health-ui");

        return app;
    }
}