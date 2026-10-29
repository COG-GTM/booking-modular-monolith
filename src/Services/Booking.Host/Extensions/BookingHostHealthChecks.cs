using BuildingBlocks.EventStoreDB;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using EventStore.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using RabbitMQ.Client;

namespace Booking.Host.Extensions;

public static class BookingHostHealthChecks
{
    public static IServiceCollection AddBookingHostHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var checks = services.AddHealthChecks();
        checks.Add(
            new HealthCheckRegistration(
                "booking-postgres",
                serviceProvider => new BookingPostgresHealthCheck(serviceProvider.GetRequiredService<IConfiguration>()),
                HealthStatus.Unhealthy,
                ["ready"]
            )
        );
        checks.Add(
            new HealthCheckRegistration(
                "booking-mongo",
                serviceProvider => new BookingMongoHealthCheck(
                    serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>()
                ),
                HealthStatus.Unhealthy,
                ["ready"]
            )
        );
        checks.Add(
            new HealthCheckRegistration(
                "booking-eventstore",
                serviceProvider => new BookingEventStoreHealthCheck(
                    serviceProvider.GetRequiredService<IConfiguration>()
                ),
                HealthStatus.Unhealthy,
                ["ready"]
            )
        );

        if (BuildingBlocks.MassTransit.Extensions.ResolveTransport(configuration) == TransportType.RabbitMq)
        {
            checks.Add(
                new HealthCheckRegistration(
                    "booking-rabbitmq",
                    serviceProvider => new BookingRabbitMqHealthCheck(
                        serviceProvider.GetRequiredService<IConfiguration>()
                    ),
                    HealthStatus.Unhealthy,
                    ["ready"]
                )
            );
        }

        return services;
    }

    private sealed class BookingPostgresHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            var connectionString =
                configuration.GetConnectionString("booking-persist-message")
                ?? configuration["PersistMessageOptions:ConnectionString:Booking"]
                ?? configuration.GetConnectionString("booking")
                ?? configuration["PostgresOptions:ConnectionString:Booking"];

            if (string.IsNullOrWhiteSpace(connectionString))
                return HealthCheckResult.Unhealthy("No Booking PostgreSQL connection string is configured.");

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy("Booking PostgreSQL is unavailable.", exception);
            }
        }
    }

    private sealed class BookingMongoHealthCheck(IOptionsMonitor<MongoOptions> options) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var mongoOptions = options.Get("Booking");
                var database = new MongoClient(mongoOptions.ConnectionString).GetDatabase(mongoOptions.DatabaseName);
                await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: cancellationToken
                );
                return HealthCheckResult.Healthy();
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy("Booking MongoDB is unavailable.", exception);
            }
        }
    }

    private sealed class BookingEventStoreHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            var connectionString =
                configuration.GetConnectionString("eventstore") ?? configuration["EventStoreOptions:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
                return HealthCheckResult.Unhealthy("No EventStoreDB connection string is configured.");

            try
            {
                using var client = new EventStoreClient(EventStoreClientSettings.Create(connectionString));
                await foreach (
                    var _ in client
                        .ReadAllAsync(
                            Direction.Forwards,
                            Position.Start,
                            maxCount: 1,
                            cancellationToken: cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                    break;

                return HealthCheckResult.Healthy();
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy("Booking EventStoreDB is unavailable.", exception);
            }
        }
    }

    private sealed class BookingRabbitMqHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var rabbitMqOptions = configuration.GetSection("RabbitMqOptions").Get<RabbitMqOptions>();
                var uri = RabbitMqHealthUri.Resolve(
                    configuration.GetConnectionString("rabbitmq"),
                    rabbitMqOptions?.HostName,
                    rabbitMqOptions?.Port,
                    rabbitMqOptions?.VirtualHost,
                    rabbitMqOptions?.UserName,
                    rabbitMqOptions?.Password
                );
                var factory = new ConnectionFactory { Uri = uri };
                await using var connection = await factory.CreateConnectionAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy("Booking RabbitMQ is unavailable.", exception);
            }
        }
    }
}
