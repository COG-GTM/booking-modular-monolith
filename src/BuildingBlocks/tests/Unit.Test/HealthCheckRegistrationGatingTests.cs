using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class HealthCheckRegistrationGatingTests
{
    private static IReadOnlyList<string> RegisteredCheckNames(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomHealthCheck();

        return services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(r => r.Name)
            .ToList();
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData(null)]
    public void rabbitmq_check_is_not_registered_unless_the_transport_is_rabbitmq(string? transport)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@rabbitmq:5672",
            ["MessageBroker:RabbitMq:HostName"] = "rabbitmq",
        };
        if (transport is not null)
            settings["MessageBroker:TransportType"] = transport;

        RegisteredCheckNames(settings).Should().BeEquivalentTo("self");
    }

    [Fact]
    public void stores_with_an_empty_connection_string_register_no_check()
    {
        var names = RegisteredCheckNames(
            new()
            {
                ["PostgresOptions:ConnectionString:Flight"] = "",
                ["PostgresOptions:ConnectionString:Passenger"] = "Host=db;Database=passenger",
                ["PersistMessageOptions:ConnectionString:Flight"] = "",
                ["EventStoreOptions:ConnectionString"] = "",
                ["MongoOptions:ConnectionString"] = "",
            }
        );

        names.Should().BeEquivalentTo("self", "postgres-passenger");
    }
}
