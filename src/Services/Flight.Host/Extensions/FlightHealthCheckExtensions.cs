using System.Data.Common;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using RabbitMQ.Client;

namespace Flight.Host.Extensions;

public static class FlightHealthCheckExtensions
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddFlightReadinessChecks(this IServiceCollection services)
    {
        services.AddTransient<FlightPostgresReadinessCheck>();
        services.AddTransient<FlightMongoReadinessCheck>();
        services.AddTransient<RabbitMqReadinessCheck>();
        services.AddSingleton<IMongoClient>(serviceProvider =>
            new MongoClient(serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Flight").ConnectionString)
        );

        services
            .AddHealthChecks()
            .AddCheck<FlightPostgresReadinessCheck>(
                "flight-postgres",
                failureStatus: HealthStatus.Unhealthy,
                timeout: CheckTimeout,
                tags: ["ready"]
            )
            .AddCheck<FlightMongoReadinessCheck>(
                "flight-mongo",
                failureStatus: HealthStatus.Unhealthy,
                timeout: CheckTimeout,
                tags: ["ready"]
            )
            .AddCheck<RabbitMqReadinessCheck>(
                "rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                timeout: CheckTimeout,
                tags: ["ready"]
            );

        return services;
    }
}

public sealed class FlightPostgresReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var connectionString =
            configuration.GetConnectionString("flight") ?? configuration["PostgresOptions:ConnectionString:Flight"];

        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Unhealthy("Flight PostgreSQL connection is not configured.");

        try
        {
            await using DbConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Flight PostgreSQL is unavailable.");
        }
    }
}

public sealed class FlightMongoReadinessCheck(IMongoClient client, IOptionsMonitor<MongoOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var flightOptions = options.Get("Flight");
            await client
                .GetDatabase(flightOptions.DatabaseName)
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Flight MongoDB is unavailable.");
        }
    }
}

public sealed class RabbitMqReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (
            string.Equals(
                configuration[$"{nameof(MessagingOptions)}:{nameof(MessagingOptions.Transport)}"],
                nameof(TransportType.InMemory),
                StringComparison.OrdinalIgnoreCase
            )
        )
            return HealthCheckResult.Healthy("RabbitMQ check skipped for the InMemory transport.");

        try
        {
            var uri = RabbitMqHealthUri.Resolve(
                configuration.GetConnectionString(BuildingBlocks.MassTransit.Extensions.RabbitMqConnectionName),
                configuration["RabbitMqOptions:HostName"],
                configuration.GetValue<ushort?>("RabbitMqOptions:Port"),
                configuration["RabbitMqOptions:VirtualHost"],
                configuration["RabbitMqOptions:UserName"],
                configuration["RabbitMqOptions:Password"]
            );
            var factory = new ConnectionFactory { Uri = uri };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unavailable.");
        }
    }
}
