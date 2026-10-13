using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using EasyNetQ.Management.Client;
using MassTransit;
using Passenger.Data;
using Passenger.Host;
using Xunit;

namespace Passenger.Host.Test;

// Passenger.Host in-process (WebApplicationFactory) on real Postgres, Mongo and RabbitMQ containers.
public class PassengerHostFixture : TestFixture<Program, PassengerDbContext, PassengerReadDbContext>
{
    public const string ServiceName = "passenger";
    public const string ConsumerQueue = $"{ServiceName}-register-new-user-handler";

    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalAppSettings() =>
        [
            new("MessageBroker:TransportType", nameof(TransportType.RabbitMq)),
            new("MessageBroker:ServiceName", ServiceName),
            new("MessageBroker:UseInbox", "true"),
        ];

    public ManagementClient CreateManagementClient() =>
        new(
            RabbitMqTestContainer.Hostname,
            TestContainers.RabbitMqContainerConfiguration.UserName,
            TestContainers.RabbitMqContainerConfiguration.Password,
            RabbitMqTestContainer.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.ApiPort)
        );

    // A bus that is not part of the host, standing in for Identity publishing to the shared broker.
    public IBusControl CreateExternalPublisher() =>
        Bus.Factory.CreateUsingRabbitMq(cfg =>
            cfg.Host(
                RabbitMqTestContainer.Hostname,
                RabbitMqTestContainer.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.Port),
                "/",
                h =>
                {
                    h.Username(TestContainers.RabbitMqContainerConfiguration.UserName);
                    h.Password(TestContainers.RabbitMqContainerConfiguration.Password);
                }
            )
        );
}

[CollectionDefinition(Name)]
public class PassengerHostCollection : ICollectionFixture<PassengerHostFixture>
{
    public const string Name = "Passenger.Host";
}
