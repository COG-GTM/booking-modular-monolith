using BuildingBlocks.HealthCheck;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace Passenger.Host.Health;

public static class PassengerHealthCheckExtensions
{
    public static IServiceCollection AddPassengerHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var healthChecks = services.AddHealthChecks();

        healthChecks.AddNpgSql(
            serviceProvider =>
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                return config.GetConnectionString("passenger")
                    ?? config["PostgresOptions:ConnectionString:Passenger"]
                    ?? string.Empty;
            },
            name: "passenger-db",
            timeout: TimeSpan.FromSeconds(10)
        );

        healthChecks.AddMongoDb(
            clientFactory: serviceProvider =>
            {
                var mongoOptions = serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Passenger");
                return new MongoClient(mongoOptions.ConnectionString);
            },
            databaseNameFactory: serviceProvider =>
                serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Passenger").DatabaseName,
            name: "passenger-read-db",
            failureStatus: HealthStatus.Unhealthy,
            timeout: TimeSpan.FromSeconds(10)
        );

        if (global::BuildingBlocks.MassTransit.Extensions.ResolveTransport(configuration) == TransportType.RabbitMq)
        {
            healthChecks.AddRabbitMQ(
                serviceProvider =>
                {
                    var config = serviceProvider.GetRequiredService<IConfiguration>();
                    var port = config.GetValue<ushort?>("RabbitMqOptions:Port");
                    var uri = RabbitMqHealthUri.Resolve(
                        config.GetConnectionString(
                            global::BuildingBlocks.MassTransit.Extensions.RabbitMqConnectionName
                        ),
                        config["RabbitMqOptions:HostName"],
                        port,
                        config["RabbitMqOptions:VirtualHost"],
                        config["RabbitMqOptions:UserName"],
                        config["RabbitMqOptions:Password"]
                    );
                    var factory = new ConnectionFactory { Uri = uri };
                    return factory.CreateConnectionAsync();
                },
                name: "rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                timeout: TimeSpan.FromSeconds(10)
            );
        }

        return services;
    }
}
