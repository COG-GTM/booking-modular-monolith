using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Host.Extensions;
using Identity.Host.Integration.Test;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;
using IdentityContext = global::Identity.Data.IdentityContext;
using IdentityHostProgram = global::Identity.Host.Program;

namespace Identity.Host.Integration.Test.Identity;

public class HostReadinessCheckTests(TestWriteFixture<IdentityHostProgram, IdentityContext> integrationTestFactory)
    : IdentityHostIntegrationTestBase(integrationTestFactory)
{
    private const string PostgresCheck = "identity-postgres";
    private const string RabbitMqCheck = "identity-rabbitmq";
    private const string LivenessCheck = "self";
    private const string ReadyTag = "ready";
    private const string LiveTag = "live";
    private const string UnreachableHost = "localhost";
    private const string UnreachablePort = "1";

    [Fact]
    public async Task health_report_should_include_healthy_identity_readiness_checks()
    {
        var healthCheckService = Fixture.ServiceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey(PostgresCheck).WhoseValue.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey(RabbitMqCheck).WhoseValue.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey(LivenessCheck).WhoseValue.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void identity_readiness_checks_should_be_tagged_ready_but_not_live()
    {
        var registrations = Fixture
            .ServiceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        registrations
            .Should()
            .ContainSingle(x => x.Name == PostgresCheck)
            .Which.Tags.Should()
            .Contain(ReadyTag)
            .And.NotContain(LiveTag);
        registrations
            .Should()
            .ContainSingle(x => x.Name == RabbitMqCheck)
            .Which.Tags.Should()
            .Contain(ReadyTag)
            .And.NotContain(LiveTag);
        registrations.Where(x => x.Tags.Contains(LiveTag)).Should().OnlyContain(x => x.Name == LivenessCheck);
    }

    [Fact]
    public async Task liveness_probe_should_not_evaluate_identity_readiness_checks()
    {
        var healthCheckService = Fixture.ServiceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync(x => x.Tags.Contains(LiveTag));

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Keys.Should().Contain(LivenessCheck);
        report.Entries.Keys.Should().NotContain(PostgresCheck);
        report.Entries.Keys.Should().NotContain(RabbitMqCheck);
    }

    [Fact]
    public async Task rabbitmq_readiness_check_should_prefer_the_rabbitmq_connection_string_over_options()
    {
        await using var app = BuildHost(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = Fixture.RabbitMqTestContainer.GetConnectionString(),
                ["RabbitMqOptions:HostName"] = UnreachableHost,
                ["RabbitMqOptions:Port"] = UnreachablePort,
            }
        );

        var report = await CheckAsync(app, RabbitMqCheck);

        report.Entries.Should().ContainKey(RabbitMqCheck).WhoseValue.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task rabbitmq_readiness_check_should_fall_back_to_rabbitmq_options_without_connection_string()
    {
        await using var app = BuildHost(new Dictionary<string, string?>());

        var report = await CheckAsync(app, RabbitMqCheck);

        report.Entries.Should().ContainKey(RabbitMqCheck).WhoseValue.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task rabbitmq_readiness_check_should_be_unhealthy_when_broker_is_unreachable()
    {
        await using var app = BuildHost(
            new Dictionary<string, string?>
            {
                ["RabbitMqOptions:HostName"] = UnreachableHost,
                ["RabbitMqOptions:Port"] = UnreachablePort,
            }
        );

        var report = await CheckAsync(app, RabbitMqCheck);

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries.Should().ContainKey(RabbitMqCheck).WhoseValue.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task rabbitmq_readiness_check_should_not_be_registered_for_in_memory_transport()
    {
        await using var app = BuildHost(
            new Dictionary<string, string?> { ["MessageBroker:TransportType"] = nameof(TransportType.InMemory) }
        );

        var registrations = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Should().NotContain(x => x.Name == RabbitMqCheck);
        registrations.Should().ContainSingle(x => x.Name == PostgresCheck);
    }

    private WebApplication BuildHost(IDictionary<string, string?> overrides)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AppOptions:Name"] = Fixture.Configuration["AppOptions:Name"],
                ["ObservabilityOptions:InstrumentationName"] = Fixture.Configuration[
                    "ObservabilityOptions:InstrumentationName"
                ],
                ["HealthOptions:Enabled"] = "false",
                ["Jwt:Authority"] = Fixture.Configuration["Jwt:Authority"],
                ["Jwt:Audience"] = Fixture.Configuration["Jwt:Audience"],
                ["MessageBroker:TransportType"] = nameof(TransportType.RabbitMq),
                ["MessageBroker:ServiceName"] = Fixture.Configuration["MessageBroker:ServiceName"],
                ["PostgresOptions:ConnectionString:Identity"] = Fixture.Configuration[
                    "PostgresOptions:ConnectionString:Identity"
                ],
                ["RabbitMqOptions:HostName"] = Fixture.Configuration["RabbitMqOptions:HostName"],
                ["RabbitMqOptions:Port"] = Fixture.Configuration["RabbitMqOptions:Port"],
                ["RabbitMqOptions:UserName"] = Fixture.Configuration["RabbitMqOptions:UserName"],
                ["RabbitMqOptions:Password"] = Fixture.Configuration["RabbitMqOptions:Password"],
                ["RabbitMqOptions:ExchangeName"] = Fixture.Configuration["RabbitMqOptions:ExchangeName"],
            }
        );
        builder.Configuration.AddInMemoryCollection(overrides);

        builder.AddIdentityHostInfrastructure();

        return builder.Build();
    }

    private static Task<HealthReport> CheckAsync(WebApplication app, string checkName)
    {
        return app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(x => x.Name == checkName);
    }
}
