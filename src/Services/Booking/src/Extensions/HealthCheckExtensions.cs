using BuildingBlocks.EFCore;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.Web;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace Booking.Host.Extensions;

public static class HealthCheckExtensions
{
    public const string PostgresCheckName = "booking-postgres";
    public const string MongoCheckName = "booking-mongo";
    public const string EventStoreCheckName = "booking-eventstore";
    public const string RabbitMqCheckName = "booking-rabbitmq";

    private static readonly string[] ReadinessTags = [BuildingBlocks.Grpc.Extensions.ReadinessTag];

    /// <summary>
    /// Readiness checks for the stores and broker owned by the Booking service. Flight/Passenger gRPC
    /// dependency checks are registered by the Booking module alongside its gRPC clients.
    /// </summary>
    public static IHealthChecksBuilder AddBookingStoreHealthChecks(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var eventStoreOptions = builder.Services.GetOptions<EventStoreOptions>(nameof(EventStoreOptions));
        var rabbitMqOptions = builder.Services.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        var eventStoreConnectionString =
            configuration.GetConnectionString("eventstore") ?? eventStoreOptions.ConnectionString;
        var rabbitMqConnectionString =
            configuration.GetConnectionString("rabbitmq")
            ?? $"amqp://{rabbitMqOptions.UserName}:{rabbitMqOptions.Password}@{rabbitMqOptions.HostName}:{rabbitMqOptions.Port ?? 5672}";

        return builder
            .Services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetPostgresConnectionString(nameof(Booking)),
                name: PostgresCheckName,
                tags: ReadinessTags
            )
            .AddMongoDb(
                clientFactory: sp =>
                    new MongoClient(
                        sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Booking)).ConnectionString
                    ),
                name: MongoCheckName,
                failureStatus: HealthStatus.Unhealthy,
                tags: ReadinessTags
            )
            .AddEventStore(eventStoreConnectionString, name: EventStoreCheckName, tags: ReadinessTags)
            .AddRabbitMQ(
                _ => new ConnectionFactory { Uri = new Uri(rabbitMqConnectionString) }.CreateConnectionAsync(),
                name: RabbitMqCheckName,
                tags: ReadinessTags
            );
    }
}
