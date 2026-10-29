using System.Collections.Concurrent;
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

        services
            .AddHealthChecks()
            .AddCheck<FlightPostgresReadinessCheck>(
                "flight-postgres",
                failureStatus: HealthStatus.Unhealthy,
                timeout: CheckTimeout,
                tags: ["ready"]
            )
            .AddCheck<FlightPostgresReadinessCheck>(
                "flight-outbox-postgres",
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

public static class FlightConnectionStrings
{
    public static string? ResolveModule(IConfiguration configuration) =>
        configuration.GetConnectionString("flight") ?? configuration["PostgresOptions:ConnectionString:Flight"];

    public static string? ResolveOutbox(IConfiguration configuration) =>
        configuration.GetConnectionString("flight-persist-message")
        ?? configuration["PersistMessageOptions:ConnectionString:Flight"]
        ?? ResolveModule(configuration);
}

public sealed class FlightPostgresReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    private const string OutboxCheckName = "flight-outbox-postgres";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var moduleConnectionString = FlightConnectionStrings.ResolveModule(configuration);
        var outboxConnectionString = FlightConnectionStrings.ResolveOutbox(configuration);
        var outboxCheck = string.Equals(context.Registration.Name, OutboxCheckName, StringComparison.Ordinal);

        if (outboxCheck)
        {
            if (string.IsNullOrWhiteSpace(outboxConnectionString))
                return HealthCheckResult.Unhealthy("Flight outbox PostgreSQL connection is not configured.");

            if (string.Equals(moduleConnectionString, outboxConnectionString, StringComparison.Ordinal))
                return HealthCheckResult.Healthy("Flight outbox shares the Flight module PostgreSQL database.");

            return await CheckConnectionAsync(outboxConnectionString, "Flight outbox PostgreSQL", cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(moduleConnectionString))
            return HealthCheckResult.Unhealthy("Flight module PostgreSQL connection is not configured.");

        return await CheckConnectionAsync(moduleConnectionString, "Flight module PostgreSQL", cancellationToken);
    }

    private static async Task<HealthCheckResult> CheckConnectionAsync(
        string connectionString,
        string storeName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using DbConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy($"{storeName} is unavailable.");
        }
    }
}

public sealed class FlightMongoReadinessCheck(IOptionsMonitor<MongoOptions> options) : IHealthCheck
{
    private static readonly ConcurrentDictionary<string, MongoClient> Clients = new(StringComparer.Ordinal);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var flightOptions = options.Get("Flight");
            var connectionString = flightOptions.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                return HealthCheckResult.Unhealthy("Flight MongoDB connection is not configured.");

            var client = Clients.GetOrAdd(connectionString, static value => new MongoClient(value));
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
