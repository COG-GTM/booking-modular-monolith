using Booking.Host.Extensions;
using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;
using MassTransitExtensions = BuildingBlocks.MassTransit.Extensions;

namespace Booking.Host.Test;

// Runs the "booking-rabbitmq" readiness check against the fixture's RabbitMQ container to pin the connection
// precedence shared with the bus: Aspire ConnectionStrings:rabbitmq first, then MessageBroker:RabbitMq.
[Collection(BookingHostCollection.Name)]
public class BookingRabbitMqReadinessTests
{
    private const string UnreachableHost = "127.0.0.1";
    private const string UnreachablePort = "1";

    private readonly BookingHostFixture _fixture;

    public BookingRabbitMqReadinessTests(BookingHostFixture fixture)
    {
        _fixture = fixture;
    }

    private string ContainerHost => _fixture.RabbitMqTestContainer.Hostname;

    private string ContainerPort =>
        _fixture
            .RabbitMqTestContainer.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.Port)
            .ToString();

    private static async Task<HealthCheckResult> RunRabbitMqCheckAsync(Dictionary<string, string?> settings)
    {
        settings["MessageBroker:TransportType"] = nameof(TransportType.RabbitMq);
        settings["PersistMessageOptions:ConnectionString:Booking"] = "Server=localhost;Database=unused";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(
            configuration.GetSection(MessageBrokerOptions.SectionName).Get<MessageBrokerOptions>() ?? new()
        );
        services.AddBookingReadinessChecks(configuration);

        await using var provider = services.BuildServiceProvider();
        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == "booking-rabbitmq");
        var check = registration.Factory(provider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        return await check.CheckHealthAsync(new HealthCheckContext { Registration = registration }, timeout.Token);
    }

    [Fact]
    public async Task should_prefer_the_aspire_rabbitmq_connection_string_over_message_broker_options()
    {
        var result = await RunRabbitMqCheckAsync(
            new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{MassTransitExtensions.RabbitMqConnectionName}"] =
                    _fixture.RabbitMqTestContainer.GetConnectionString(),
                ["MessageBroker:RabbitMq:Host"] = UnreachableHost,
                ["MessageBroker:RabbitMq:Port"] = UnreachablePort,
            }
        );

        result.Status.Should().Be(HealthStatus.Healthy, result.Exception?.ToString());
    }

    [Fact]
    public async Task should_fall_back_to_message_broker_options_when_no_connection_string_is_configured()
    {
        var result = await RunRabbitMqCheckAsync(
            new Dictionary<string, string?>
            {
                ["MessageBroker:RabbitMq:Host"] = ContainerHost,
                ["MessageBroker:RabbitMq:Port"] = ContainerPort,
                ["MessageBroker:RabbitMq:Username"] = TestContainers.RabbitMqContainerConfiguration.UserName,
                ["MessageBroker:RabbitMq:Password"] = TestContainers.RabbitMqContainerConfiguration.Password,
            }
        );

        result.Status.Should().Be(HealthStatus.Healthy, result.Exception?.ToString());
    }

    [Fact]
    public async Task should_report_unhealthy_when_the_configured_broker_is_unreachable()
    {
        var result = await RunRabbitMqCheckAsync(
            new Dictionary<string, string?>
            {
                ["MessageBroker:RabbitMq:Host"] = UnreachableHost,
                ["MessageBroker:RabbitMq:Port"] = UnreachablePort,
            }
        );

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}
