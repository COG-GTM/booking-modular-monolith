using Booking;
using Booking.Extensions.Infrastructure;
using Booking.Host.Extensions;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using EventStore.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Host.Test;

public class BookingHealthCheckExtensionsTests
{
    [Fact]
    public async Task rabbitmq_readiness_prefers_connection_string_and_reports_wrong_scheme()
    {
        var result = await RunRabbitMqCheckAsync(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "http://not-amqp:5672",
                ["MessageBroker:RabbitMq:Host"] = "127.0.0.1",
                ["MessageBroker:RabbitMq:Port"] = "1",
            }
        );

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<ArgumentException>();
        result.Exception!.Message.Should().Contain("Wrong scheme");
    }

    [Fact]
    public async Task rabbitmq_readiness_falls_back_to_message_broker_options_when_connection_string_is_empty()
    {
        var result = await RunRabbitMqCheckAsync(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "",
                ["MessageBroker:RabbitMq:Host"] = "127.0.0.1",
                ["MessageBroker:RabbitMq:Port"] = "1",
            }
        );

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
        result.Exception.Should().NotBeOfType<ArgumentException>();
        result.Exception!.ToString().Should().NotContain("Wrong scheme");
    }

    [Fact]
    public void readiness_registrations_are_tagged_unhealthy_and_eventstore_factory_resolves_check()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["MessageBroker:TransportType"] = nameof(TransportType.InMemory),
                ["EventStoreOptions:ConnectionString"] = "esdb://localhost:2113?tls=false",
            }
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new EventStoreClient(EventStoreClientSettings.Create("esdb://localhost:2113?tls=false")));
        services.AddBookingReadinessChecks(configuration);

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var bookingRegistrations = registrations
            .Where(registration => registration.Name.StartsWith("booking-", StringComparison.Ordinal))
            .ToArray();
        bookingRegistrations
            .Select(registration => registration.Name)
            .Should()
            .BeEquivalentTo("booking-persist-message-postgres", "booking-read-mongo", "booking-eventstore");
        bookingRegistrations
            .Should()
            .OnlyContain(registration =>
                registration.Tags.Contains(BookingHealthCheckExtensions.ReadyTag)
                && registration.FailureStatus == HealthStatus.Unhealthy
            );

        var eventStoreRegistration = registrations.Single(registration => registration.Name == "booking-eventstore");
        eventStoreRegistration.Factory(provider).Should().BeOfType<EventStoreDbHealthCheck>();
    }

    [Fact]
    public async Task eventstore_readiness_reports_unreachable_port_as_unhealthy()
    {
        var client = new EventStoreClient(EventStoreClientSettings.Create("esdb://127.0.0.1:1?tls=false"));
        var check = new EventStoreDbHealthCheck(client);
        var registration = new HealthCheckRegistration(
            "booking-eventstore",
            check,
            HealthStatus.Unhealthy,
            [BookingHealthCheckExtensions.ReadyTag]
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext { Registration = registration },
            timeout.Token
        );

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public void host_background_switch_controls_outbox_and_eventstore_workers()
    {
        var enabledBuilder = CreateHostBuilder();
        enabledBuilder.AddBookingHostInfrastructure();
        enabledBuilder.AddBookingModules();

        enabledBuilder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>)
            );
        enabledBuilder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationFactory != null
            );

        var disabledBuilder = CreateHostBuilder(
            new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "false" }
        );
        disabledBuilder.AddBookingHostInfrastructure();
        disabledBuilder.AddBookingModules();

        disabledBuilder
            .Services.Should()
            .NotContain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>)
            );
        disabledBuilder
            .Services.Should()
            .NotContain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationFactory != null
            );
    }

    private static async Task<HealthCheckResult> RunRabbitMqCheckAsync(Dictionary<string, string?> settings)
    {
        settings["MessageBroker:TransportType"] = nameof(TransportType.RabbitMq);
        var configuration = CreateConfiguration(settings);
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
            .Value.Registrations.Single(item => item.Name == "booking-rabbitmq");
        var check = registration.Factory(provider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        return await check.CheckHealthAsync(new HealthCheckContext { Registration = registration }, timeout.Token);
    }

    private static IConfigurationRoot CreateConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static WebApplicationBuilder CreateHostBuilder(Dictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "test",
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
                ["ObservabilityOptions:UseGrafanaExporter"] = "false",
            }
        );
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }
        return builder;
    }
}
