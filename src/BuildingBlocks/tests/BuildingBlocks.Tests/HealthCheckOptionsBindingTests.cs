using BuildingBlocks.EFCore;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Tests;

public class HealthCheckOptionsBindingTests
{
    [Fact]
    public void enabled_health_checks_register_all_configured_dependencies()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["HealthOptions:Enabled"] = "true",
                ["AppOptions:Name"] = "test-app",
                ["PostgresOptions:ConnectionString"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["RabbitMqOptions:HostName"] = "localhost",
                ["RabbitMqOptions:UserName"] = "guest",
                ["RabbitMqOptions:Password"] = "guest",
                ["EventStoreOptions:ConnectionString"] = "esdb://localhost:2113",
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
            }
        );

        RegistrationNames(provider)
            .Should()
            .BeEquivalentTo(["self", "rabbitmq", "MongoDB-Health", "npgsql", "eventstore"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void module_postgres_checks_are_distinct_and_prefer_aspire_connections(bool useAspire)
    {
        var settings = new Dictionary<string, string?>
        {
            ["HealthOptions:Enabled"] = "true",
            ["AppOptions:Name"] = "test-app",
            ["PostgresOptions:ConnectionString:Flight"] = useAspire
                ? "UnsupportedKeyword=must-not-be-used"
                : "Host=localhost;Database=flight",
            ["PostgresOptions:ConnectionString:Identity"] = "Host=localhost;Database=identity",
            ["PostgresOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger",
            ["PostgresOptions:ConnectionString:Booking"] = "Host=localhost;Database=booking",
            ["PostgresOptions:ConnectionString:Empty"] = "",
        };
        if (useAspire)
            settings["ConnectionStrings:flight"] = "Host=localhost;Database=aspire_flight";

        using var provider = CreateProvider(settings);
        RegistrationNames(provider)
            .Should()
            .BeEquivalentTo(
                ["self", "rabbitmq", "Postgres-Flight", "Postgres-Identity", "Postgres-Passenger", "Postgres-Booking"]
            );

        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        foreach (var check in checks.Where(check => check.Name.StartsWith("Postgres-")))
            check.Factory(provider).Should().BeOfType<global::HealthChecks.NpgSql.NpgSqlHealthCheck>();
    }

    [Fact]
    public void rabbitmq_is_registered_without_unconfigured_database_checks()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["HealthOptions:Enabled"] = "true",
                ["AppOptions:Name"] = "test-app",
                ["RabbitMqOptions:HostName"] = "localhost",
                ["RabbitMqOptions:UserName"] = "guest",
                ["RabbitMqOptions:Password"] = "guest",
            }
        );

        RegistrationNames(provider).Should().BeEquivalentTo(["self", "rabbitmq"]);
    }

    [Fact]
    public void disabled_health_checks_register_only_self()
    {
        using var provider = CreateProvider(new Dictionary<string, string?> { ["HealthOptions:Enabled"] = "false" });

        RegistrationNames(provider).Should().BeEquivalentTo(["self"]);
    }

    [Fact]
    public void dependency_options_use_the_sections_and_properties_bound_by_health_checks()
    {
        nameof(PostgresOptions).Should().Be("PostgresOptions");
        typeof(PostgresOptions)
            .GetProperty(nameof(PostgresOptions.ConnectionString))!
            .PropertyType.Should()
            .Be<string>();
        nameof(MongoOptions).Should().Be("MongoOptions");
        typeof(MongoOptions).GetProperty(nameof(MongoOptions.ConnectionString))!.PropertyType.Should().Be<string>();
        nameof(EventStoreOptions).Should().Be("EventStoreOptions");
        typeof(EventStoreOptions)
            .GetProperty(nameof(EventStoreOptions.ConnectionString))!
            .PropertyType.Should()
            .Be<string>();
        nameof(RabbitMqOptions).Should().Be("RabbitMqOptions");
        typeof(RabbitMqOptions).GetProperty(nameof(RabbitMqOptions.HostName))!.PropertyType.Should().Be<string>();
        typeof(RabbitMqOptions).GetProperty(nameof(RabbitMqOptions.UserName))!.PropertyType.Should().Be<string>();
        typeof(RabbitMqOptions).GetProperty(nameof(RabbitMqOptions.Password))!.PropertyType.Should().Be<string>();
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddCustomHealthCheck();
        return services.BuildServiceProvider();
    }

    private static string[] RegistrationNames(IServiceProvider provider) =>
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name)
            .Distinct()
            .ToArray();
}
