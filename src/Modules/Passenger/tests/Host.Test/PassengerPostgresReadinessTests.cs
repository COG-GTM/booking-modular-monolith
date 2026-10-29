using System.Reflection;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Passenger.Host.Health;
using Xunit;

namespace Passenger.Host.Test;

public sealed class PassengerPostgresReadinessTests
{
    [Theory]
    [InlineData("outbox_discovery", "outbox_options", "write_discovery", "outbox_discovery")]
    [InlineData(null, "outbox_options", "write_discovery", "outbox_options")]
    [InlineData(null, null, "write_discovery", "write_discovery")]
    [InlineData(null, null, null, "write_options")]
    public async Task outbox_probe_uses_the_same_connection_precedence_as_the_persist_message_context(
        string? outboxConnection,
        string? outboxOptions,
        string? writeConnection,
        string expectedDatabase
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(Settings(outboxConnection, outboxOptions, writeConnection));
        builder.Services.AddPassengerHealthChecks(builder.Configuration);
        builder.AddPersistMessageProcessor<global::Passenger.PassengerRoot>("Passenger");
        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var probe = ProbeOptions(provider, "passenger-persist-message");
        var context = scope.ServiceProvider.GetRequiredService<
            PersistMessageDbContext<global::Passenger.PassengerRoot>
        >();

        new NpgsqlConnectionStringBuilder(probe.ConnectionString).Database.Should().Be(expectedDatabase);
        probe.ConnectionString.Should().Be(context.Database.GetConnectionString());
    }

    [Theory]
    [InlineData("write_discovery", "write_discovery")]
    [InlineData(null, "write_options")]
    public async Task write_probe_does_not_use_the_outbox_database(string? writeConnection, string expectedDatabase)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(Settings("outbox_discovery", "outbox_options", writeConnection))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPassengerHealthChecks(configuration);
        await using var provider = services.BuildServiceProvider();

        var probe = ProbeOptions(provider, "passenger-db");

        new NpgsqlConnectionStringBuilder(probe.ConnectionString).Database.Should().Be(expectedDatabase);
    }

    [Fact]
    public async Task postgres_probes_resolve_configuration_when_they_are_created()
    {
        var registrationConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(Settings("registration_outbox", null, "registration_write"))
            .Build();
        var runtimeConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(Settings("runtime_outbox", null, "runtime_write"))
            .Build();
        var services = new ServiceCollection();
        services.AddPassengerHealthChecks(registrationConfiguration);
        services.AddSingleton<IConfiguration>(runtimeConfiguration);
        await using var provider = services.BuildServiceProvider();

        new NpgsqlConnectionStringBuilder(ProbeOptions(provider, "passenger-db").ConnectionString)
            .Database.Should()
            .Be("runtime_write");
        new NpgsqlConnectionStringBuilder(ProbeOptions(provider, "passenger-persist-message").ConnectionString)
            .Database.Should()
            .Be("runtime_outbox");
    }

    private static Dictionary<string, string?> Settings(
        string? outboxConnection,
        string? outboxOptions,
        string? writeConnection
    ) =>
        new()
        {
            ["MessagingOptions:Transport"] = "InMemory",
            ["ConnectionStrings:passenger-persist-message"] = ConnectionString(outboxConnection),
            ["PersistMessageOptions:ConnectionString:Passenger"] = ConnectionString(outboxOptions),
            ["ConnectionStrings:passenger"] = ConnectionString(writeConnection),
            ["PostgresOptions:ConnectionString:Passenger"] = ConnectionString("write_options"),
        };

    private static string? ConnectionString(string? database) =>
        database is null ? null : $"Host=localhost;Database={database};Username=passenger";

    private static NpgSqlHealthCheckOptions ProbeOptions(IServiceProvider provider, string name)
    {
        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(registration => registration.Name == name);
        var check = registration.Factory(provider).Should().BeOfType<NpgSqlHealthCheck>().Subject;
        return (NpgSqlHealthCheckOptions)
            typeof(NpgSqlHealthCheck)
                .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(check)!;
    }
}
