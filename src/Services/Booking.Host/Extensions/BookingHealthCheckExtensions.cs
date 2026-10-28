using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace Booking.Host.Extensions;

public static class BookingHealthCheckExtensions
{
    public static IServiceCollection AddBookingHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment
    )
    {
        var checks = services.AddHealthChecks();
        checks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
        services
            .AddOptions<HealthCheckServiceOptions>()
            .Configure<IConfiguration>(
                (options, _) =>
                {
                    options.Registrations.Add(
                        new HealthCheckRegistration(
                            "postgres-booking-persist-message",
                            sp => new NpgSqlHealthCheck(
                                new NpgSqlHealthCheckOptions(
                                    sp.GetRequiredService<IConfiguration>()
                                        .ResolvePersistMessageConnectionString("Booking")
                                )
                            ),
                            HealthStatus.Unhealthy,
                            ["ready"]
                        )
                    );
                }
            );
        checks.AddMongoDb(
            sp => new MongoClient(
                sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Booking").ConnectionString
            ),
            name: "mongo-booking",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready"]
        );

        services.AddHttpClient<EventStoreHealthCheck>(client => client.Timeout = TimeSpan.FromSeconds(3));
        checks.AddCheck<EventStoreHealthCheck>("eventstore", tags: ["ready"]);
        if (configuration.IsModuleBackgroundProcessingEnabled("Booking"))
            checks.AddCheck<EventStoreDBSubscriptionHealthCheck>("eventstore-subscription", tags: ["ready"]);

        var transport =
            configuration.GetValue<TransportType?>("Messaging:Transport")
            ?? (environment.IsEnvironment("test") ? TransportType.InMemory : TransportType.RabbitMq);
        if (transport == TransportType.RabbitMq)
        {
            checks.AddRabbitMQ(
                sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var connectionString = config.GetConnectionString("rabbitmq");
                    var factory = new ConnectionFactory();
                    if (!string.IsNullOrWhiteSpace(connectionString))
                        factory.Uri = new Uri(connectionString);
                    else
                    {
                        var options = config.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>() ?? new();
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
                tags: ["ready"]
            );
        }

        return services;
    }

    public static WebApplication MapBookingHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready").AllowAnonymous();
        return app;
    }
}

public sealed class EventStoreDBSubscriptionHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var state = serviceProvider.GetService<EventStoreDBSubscriptionState>();
        return Task.FromResult(
            state?.IsSubscribed == true
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("EventStoreDB $all subscription is not active")
        );
    }
}

public sealed class EventStoreHealthCheck(HttpClient httpClient, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var connectionString =
            configuration.GetConnectionString("eventstore") ?? configuration["EventStoreOptions:ConnectionString"];
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var eventStoreAddress))
            return HealthCheckResult.Unhealthy("EventStoreDB connection string is invalid.");

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(eventStoreAddress.Query);
        var tls = !query.TryGetValue("tls", out var tlsValue) || !bool.TryParse(tlsValue, out var enabled) || enabled;
        var port = eventStoreAddress.IsDefaultPort ? 2113 : eventStoreAddress.Port;
        var healthAddress = new UriBuilder(tls ? "https" : "http", eventStoreAddress.Host, port, "/health/live").Uri;

        try
        {
            using var response = await httpClient.GetAsync(healthAddress, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"EventStoreDB returned {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("EventStoreDB health endpoint is unavailable.", exception);
        }
    }
}
