using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Host.Extensions;
using FluentAssertions;
using Integration.Test;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Xunit;

namespace Host.Test;

public class FlightPostgresReadinessTests : FlightIntegrationTestBase
{
    public FlightPostgresReadinessTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task separate_unavailable_outbox_store_fails_readiness_while_module_store_is_healthy()
    {
        var moduleConnectionString = Fixture.Configuration["PostgresOptions:ConnectionString:Flight"]!;
        var unreachableOutboxConnectionString = new NpgsqlConnectionStringBuilder(moduleConnectionString)
        {
            Port = 1,
            Timeout = 1,
        }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PostgresOptions:ConnectionString:Flight"] = moduleConnectionString,
                    ["ConnectionStrings:flight-persist-message"] = unreachableOutboxConnectionString,
                }
            )
            .Build();
        var check = new FlightPostgresReadinessCheck(configuration);

        var moduleResult = await check.CheckHealthAsync(CreateContext("flight-postgres", check));
        var outboxResult = await check.CheckHealthAsync(CreateContext("flight-outbox-postgres", check));

        moduleResult.Status.Should().Be(HealthStatus.Healthy);
        outboxResult.Status.Should().Be(HealthStatus.Unhealthy);
        outboxResult.Description.Should().Contain("Flight outbox PostgreSQL");
    }

    private static HealthCheckContext CreateContext(string name, FlightPostgresReadinessCheck check) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                name,
                check,
                HealthStatus.Unhealthy,
                ["ready"],
                TimeSpan.FromSeconds(5)
            ),
        };
}
