using Booking.Data;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using EventStore.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using PersistMessageExtensions = BuildingBlocks.PersistMessageProcessor.Extensions;

namespace Booking.Host.Extensions;

// Readiness for every store Booking.Host owns. The HealthOptions-driven checks in BuildingBlocks only know the
// monolith's shared PostgresOptions, not Booking's own outbox database.
public static class BookingHealthCheckExtensions
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddBookingReadinessChecks(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var checks = services.AddHealthChecks();

        checks.AddMongoDb(
            sp => sp.GetRequiredService<BookingReadDbContext>().Database,
            name: "booking-read-mongo",
            tags: [ReadyTag],
            timeout: TimeSpan.FromSeconds(10)
        );

        checks.AddNpgSql(
            sp =>
                PersistMessageExtensions.GetPersistMessageConnectionString(
                    sp.GetRequiredService<IConfiguration>(),
                    BookingHostExtensions.BookingModuleName
                ),
            name: "booking-persist-message-postgres",
            tags: [ReadyTag]
        );

        checks.AddCheck<EventStoreDbHealthCheck>("booking-eventstore", tags: [ReadyTag]);

        var brokerOptions = configuration.GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);
        if (brokerOptions.TransportType == TransportType.RabbitMq)
        {
            checks.AddRabbitMQ(
                sp =>
                {
                    var rabbitMq = sp.GetRequiredService<MessageBrokerOptions>().RabbitMq;
                    return new ConnectionFactory
                    {
                        HostName = rabbitMq.Host,
                        Port = rabbitMq.Port,
                        VirtualHost = rabbitMq.VirtualHost,
                        UserName = rabbitMq.Username,
                        Password = rabbitMq.Password,
                    }.CreateConnectionAsync();
                },
                name: "booking-rabbitmq",
                tags: [ReadyTag]
            );
        }

        return services;
    }
}

public sealed class EventStoreDbHealthCheck(EventStoreClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var read = client.ReadAllAsync(
                Direction.Backwards,
                Position.End,
                maxCount: 1,
                cancellationToken: cancellationToken
            );
            await foreach (var _ in read)
            {
                break;
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
