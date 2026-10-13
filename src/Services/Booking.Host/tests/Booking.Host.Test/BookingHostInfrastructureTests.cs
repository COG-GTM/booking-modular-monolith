using System.Net;
using Booking.Host.Extensions;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using EventStore.Client;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using MassTransitExtensions = BuildingBlocks.MassTransit.Extensions;
using PersistMessageExtensions = BuildingBlocks.PersistMessageProcessor.Extensions;
using TransportType = BuildingBlocks.MassTransit.TransportType;

namespace Booking.Host.Test;

// Docker-free tests for the host's cross-cutting wiring (BookingHostExtensions) and its appsettings
// defaults. Nothing here boots Program, so EventStoreDB/Mongo/Postgres are never contacted.
public class BookingHostInfrastructureTests
{
    private const string Development = "Development";
    private const string Production = "Production";

    private sealed class FakeBookingCreatedHandler : IConsumer<BookingCreated>
    {
        public Task Consume(ConsumeContext<BookingCreated> context) => Task.CompletedTask;
    }

    // Loads the host's own appsettings.json / appsettings.<env>.json (copied next to the test assembly).
    private static WebApplicationBuilder CreateHostBuilder(
        string environmentName,
        Dictionary<string, string?>? overrides = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environmentName,
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

        builder.WebHost.UseTestServer();
        return builder;
    }

    [Fact]
    public void appsettings_describe_booking_owned_stores_and_service_prefixed_bus()
    {
        var configuration = CreateHostBuilder(Production).Configuration;

        configuration["AppOptions:Name"].Should().Be("Booking-Service");
        configuration["MongoOptions:Booking:DatabaseName"].Should().Be("booking_service_read");
        configuration["EventStoreOptions:ConnectionString"].Should().StartWith("esdb://");
        configuration["Grpc:FlightAddress"].Should().Be("http://localhost:5201");
        configuration["Grpc:PassengerAddress"].Should().Be("http://localhost:5301");
        configuration.IsModuleBackgroundProcessingEnabled(BookingHostExtensions.BookingModuleName).Should().BeTrue();

        PersistMessageExtensions
            .GetPersistMessageConnectionString(configuration, BookingHostExtensions.BookingModuleName)
            .Should()
            .Contain("Database=booking_service_persist_message");

        var broker = configuration.GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);
        broker.TransportType.Should().Be(TransportType.RabbitMq);
        broker.ServiceName.Should().Be("booking-service");
        broker.UseInbox.Should().BeTrue();

        MassTransitExtensions
            .CreateEndpointNameFormatter(broker)
            .Consumer<FakeBookingCreatedHandler>()
            .Should()
            .Be("booking-service-fake-booking-created-handler");
    }

    [Fact]
    public void test_environment_switches_the_bus_to_in_memory()
    {
        var builder = CreateHostBuilder("test");
        builder.AddBookingHostInfrastructure();

        using var provider = builder.Services.BuildServiceProvider();
        var broker = provider.GetRequiredService<MessageBrokerOptions>();

        broker.TransportType.Should().Be(TransportType.InMemory);
        broker.ServiceName.Should().Be("booking-service");
        broker.UseInbox.Should().BeTrue();
    }

    [Fact]
    public void host_infrastructure_registers_booking_owned_outbox_and_event_dispatching()
    {
        var builder = CreateHostBuilder("test");
        builder.AddBookingHostInfrastructure();
        var services = builder.Services;

        services
            .Where(d => d.ServiceType == typeof(PersistMessageStore))
            .Select(d => (PersistMessageStore)d.ImplementationInstance!)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new PersistMessageStore(
                    BookingHostExtensions.BookingModuleName,
                    typeof(PersistMessageDbContext<BookingRoot>)
                )
            );

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IPersistMessageProcessor<BookingRoot>) && d.Lifetime == ServiceLifetime.Scoped
            );
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IEventDispatcher<BookingRoot>)
                && d.ImplementationType == typeof(EventDispatcher<BookingRoot>)
            );
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IIntegrationEventPublisher<BookingRoot>)
                && d.ImplementationType == typeof(PersistMessageIntegrationEventPublisher<BookingRoot>)
            );
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>)
            );

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(ICurrentUserProvider)
                && d.ImplementationType == typeof(CurrentUserProvider)
                && d.Lifetime == ServiceLifetime.Scoped
            );
        services
            .Should()
            .Contain(d => d.ServiceType == typeof(AuthHeaderHandler) && d.Lifetime == ServiceLifetime.Transient);
        services.Should().Contain(d => d.ServiceType == typeof(IBus));
    }

    [Fact]
    public void disabling_background_processing_skips_the_outbox_worker()
    {
        var builder = CreateHostBuilder("test", new() { ["Modules:Booking:BackgroundProcessingEnabled"] = "false" });
        builder.AddBookingHostInfrastructure();

        builder
            .Services.Should()
            .NotContain(d => d.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>));
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IPersistMessageProcessor<BookingRoot>));
    }

    [Fact]
    public void service_defaults_register_liveness_check_and_resilient_http_clients()
    {
        var builder = CreateHostBuilder(Production);
        builder.AddBookingServiceDefaults();
        builder.Services.AddHttpClient("probe");

        using var provider = builder.Services.BuildServiceProvider();

        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations.Should().ContainSingle(r => r.Name == "self").Which.Tags.Should().Contain("live");

        var handlers = new List<string>();
        for (
            var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("probe");
            handler is DelegatingHandler delegating;
            handler = delegating.InnerHandler!
        )
        {
            handlers.Add(delegating.GetType().Name);
        }

        handlers.Should().Contain("ResilienceHandler");
        handlers.Should().Contain("ResolvingHttpDelegatingHandler", "service discovery resolves logical names");
    }

    [Theory]
    [InlineData(Production)]
    [InlineData("Staging")]
    [InlineData(Development)]
    public async Task health_probes_and_root_are_served_in_every_environment(string environmentName)
    {
        await using var app = await StartInfrastructureOnlyAppAsync(environmentName);
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/")).Should().Be("Booking-Service");
        (await client.GetAsync(BookingHostExtensions.HealthEndpointPath)).StatusCode.Should().Be(HttpStatusCode.OK);

        var alive = await client.GetAsync(BookingHostExtensions.AlivenessEndpointPath);
        alive.StatusCode.Should().Be(HttpStatusCode.OK);
        (await alive.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public void readiness_checks_cover_every_booking_owned_store_on_rabbitmq()
    {
        var builder = CreateHostBuilder(Production);
        builder.AddBookingServiceDefaults();

        using var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations
            .Where(r => r.Tags.Contains(BookingHealthCheckExtensions.ReadyTag))
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(
                "booking-read-mongo",
                "booking-persist-message-postgres",
                "booking-eventstore",
                "booking-rabbitmq"
            );
        registrations.Single(r => r.Name == "self").Tags.Should().NotContain(BookingHealthCheckExtensions.ReadyTag);
    }

    [Fact]
    public void readiness_checks_skip_rabbitmq_on_the_in_memory_transport()
    {
        var builder = CreateHostBuilder("test");
        builder.AddBookingServiceDefaults();

        using var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Should().NotContain(r => r.Name == "booking-rabbitmq");
        registrations.Should().Contain(r => r.Name == "booking-eventstore");
    }

    [Fact]
    public async Task event_store_health_check_reports_registration_failure_status_when_read_fails()
    {
        var client = new EventStoreClient(EventStoreClientSettings.Create("esdb://localhost:2113?tls=false"));
        var check = new EventStoreDbHealthCheck(client);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "booking-eventstore",
                check,
                HealthStatus.Degraded,
                [BookingHealthCheckExtensions.ReadyTag]
            ),
        };
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await check.CheckHealthAsync(context, cancelled.Token);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task unknown_route_is_reported_as_problem_details()
    {
        await using var app = await StartInfrastructureOnlyAppAsync(Production);
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // The host's cross-cutting pipeline (health, observability, problem details, correlation id, root) without
    // the Booking module, the outbox worker or the store readiness checks, so no data store is needed.
    private static async Task<WebApplication> StartInfrastructureOnlyAppAsync(string environmentName)
    {
        var builder = CreateHostBuilder(environmentName);
        builder.AddBookingServiceDefaults();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddProblemDetails();

        // The store readiness checks need the Booking module's Mongo/EventStoreDB clients; drop them so /health
        // exercises only the probe mapping.
        builder.Services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (
                var ready in options
                    .Registrations.Where(r => r.Tags.Contains(BookingHealthCheckExtensions.ReadyTag))
                    .ToList()
            )
            {
                options.Registrations.Remove(ready);
            }
        });

        var app = builder.Build();
        app.UseBookingHostInfrastructure();
        await app.StartAsync();
        return app;
    }
}
