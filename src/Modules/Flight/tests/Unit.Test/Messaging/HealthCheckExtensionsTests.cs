namespace Unit.Test.Messaging;

using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

public class HealthCheckExtensionsTests
{
    private static IReadOnlyList<string> RegisteredHealthChecks(string transportType)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["HealthOptions:Enabled"] = "true",
                    ["AppOptions:Name"] = "booking",
                    ["MessageBroker:TransportType"] = transportType,
                    ["MessageBroker:RabbitMq:Host"] = "rabbitmq",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddCustomHealthCheck();

        using var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(x => x.Name)
            .ToList();
    }

    [Fact]
    public void rabbitmq_transport_should_register_the_rabbitmq_health_check()
    {
        var names = RegisteredHealthChecks("RabbitMq");

        names.Should().Contain("self");
        names.Should().ContainSingle(n => n.Equals("rabbitmq", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void in_memory_transport_should_not_register_the_rabbitmq_health_check()
    {
        var names = RegisteredHealthChecks("InMemory");

        names.Should().Contain("self");
        names.Should().NotContain(n => n.Contains("rabbit", StringComparison.OrdinalIgnoreCase));
    }
}
