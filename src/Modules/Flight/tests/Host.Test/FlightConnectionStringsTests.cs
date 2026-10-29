using Flight.Host.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Host.Test;

public class FlightConnectionStringsTests
{
    [Fact]
    public void connection_string_outbox_override_takes_precedence()
    {
        var configuration = CreateConfiguration(
            ("ConnectionStrings:flight-persist-message", "outbox-connection-string"),
            ("PersistMessageOptions:ConnectionString:Flight", "outbox-options"),
            ("ConnectionStrings:flight", "module-connection-string"),
            ("PostgresOptions:ConnectionString:Flight", "module-options")
        );

        FlightConnectionStrings.ResolveOutbox(configuration).Should().Be("outbox-connection-string");
    }

    [Fact]
    public void persist_message_options_override_precedes_module_connection()
    {
        var configuration = CreateConfiguration(
            ("PersistMessageOptions:ConnectionString:Flight", "outbox-options"),
            ("ConnectionStrings:flight", "module-connection-string"),
            ("PostgresOptions:ConnectionString:Flight", "module-options")
        );

        FlightConnectionStrings.ResolveOutbox(configuration).Should().Be("outbox-options");
    }

    [Fact]
    public void outbox_connection_falls_back_to_module_connection_string()
    {
        var configuration = CreateConfiguration(
            ("ConnectionStrings:flight", "module-connection-string"),
            ("PostgresOptions:ConnectionString:Flight", "module-options")
        );

        FlightConnectionStrings.ResolveModule(configuration).Should().Be("module-connection-string");
        FlightConnectionStrings.ResolveOutbox(configuration).Should().Be("module-connection-string");
    }

    [Fact]
    public void outbox_connection_falls_back_to_module_postgres_options()
    {
        var configuration = CreateConfiguration(("PostgresOptions:ConnectionString:Flight", "module-options"));

        FlightConnectionStrings.ResolveModule(configuration).Should().Be("module-options");
        FlightConnectionStrings.ResolveOutbox(configuration).Should().Be("module-options");
    }

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();
}
