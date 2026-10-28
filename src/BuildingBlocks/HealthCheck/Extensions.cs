using BuildingBlocks.EFCore;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
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
            var rabbitMqOptions = services.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));
            var eventStoreOptions = services.GetOptions<EventStoreOptions>(nameof(EventStoreOptions));
            var mongoOptions = services.GetOptions<MongoOptions>(nameof(MongoOptions));

            var healthChecksBuilder = services
                .AddHealthChecks()
                .AddRabbitMQ(serviceProvider =>
                {
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(
                            $"amqp://{rabbitMqOptions.UserName}:{rabbitMqOptions.Password}@{rabbitMqOptions.HostName}"
                        ),
                    };
                    return factory.CreateConnectionAsync();
                });

            if (!string.IsNullOrEmpty(mongoOptions.ConnectionString))
            {
                healthChecksBuilder.AddMongoDb(
                    clientFactory: _ => new MongoClient(mongoOptions.ConnectionString),
                    name: "MongoDB-Health",
                    failureStatus: HealthStatus.Unhealthy,
                    timeout: TimeSpan.FromSeconds(10)
                );
            }

            services
                .AddOptions<HealthCheckServiceOptions>()
                .Configure<
                    IConfiguration,
                    IEnumerable<PostgresModuleRegistration>,
                    IEnumerable<ModulePersistenceRegistration>
                >(
                    (options, config, writeStores, persistStores) =>
                    {
                        var writeModules = config
                            .GetSection("PostgresOptions:ConnectionString")
                            .GetChildren()
                            .Select(x => x.Key)
                            .Concat(writeStores.Select(x => x.ModuleName))
                            .Distinct(StringComparer.OrdinalIgnoreCase);
                        foreach (var module in writeModules)
                            options.Registrations.Add(
                                new HealthCheckRegistration(
                                    $"postgres-{module.ToLowerInvariant()}",
                                    sp => new NpgSqlHealthCheck(
                                        new NpgSqlHealthCheckOptions(
                                            sp.GetRequiredService<IConfiguration>()
                                                .ResolvePostgresConnectionString(module)
                                        )
                                    ),
                                    HealthStatus.Unhealthy,
                                    tags: null
                                )
                            );

                        var persistenceModules = config
                            .GetSection("PersistMessageOptions:ConnectionString")
                            .GetChildren()
                            .Select(x => x.Key)
                            .Concat(
                                config
                                    .GetSection("ConnectionStrings")
                                    .GetChildren()
                                    .Where(x => x.Key.EndsWith("-persist-message", StringComparison.OrdinalIgnoreCase))
                                    .Select(x => x.Key[..^"-persist-message".Length])
                            )
                            .Concat(persistStores.Select(x => x.ModuleName))
                            .Distinct(StringComparer.OrdinalIgnoreCase);
                        foreach (var module in persistenceModules)
                            options.Registrations.Add(
                                new HealthCheckRegistration(
                                    $"postgres-{module.ToLowerInvariant()}-persist-message",
                                    sp => new NpgSqlHealthCheck(
                                        new NpgSqlHealthCheckOptions(
                                            sp.GetRequiredService<IConfiguration>()
                                                .ResolvePersistMessageConnectionString(module)
                                        )
                                    ),
                                    HealthStatus.Unhealthy,
                                    tags: null
                                )
                            );
                    }
                );

            if (!string.IsNullOrEmpty(eventStoreOptions.ConnectionString))
                healthChecksBuilder.AddEventStore(eventStoreOptions.ConnectionString);

            services
                .AddHealthChecksUI(setup =>
                {
                    setup.SetEvaluationTimeInSeconds(60); // time in seconds between check
                    setup.AddHealthCheckEndpoint($"Self Check - {appOptions.Name}", HealthEndpointPath);
                })
                .AddInMemoryStorage();
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
            app.MapHealthChecks(
                AlivenessEndpointPath,
                new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") }
            );
        }

        if (healthOptions.Enabled)
            app.MapHealthChecksUI(options => options.UIPath = "/health-ui");

        return app;
    }
}
