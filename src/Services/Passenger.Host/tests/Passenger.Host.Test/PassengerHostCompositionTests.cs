using BuildingBlocks.Exception;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using FluentAssertions;
using Grpc.AspNetCore.HealthChecks;
using Grpc.AspNetCore.Server;
using Grpc.Core.Interceptors;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Passenger.Extensions.Infrastructure;
using Passenger.Host.Extensions;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Passenger.Host.Test;

// Docker-free: composes Passenger.Host the way Program.cs does (minus Run) on an empty content root,
// so only the in-memory settings below are visible, and inspects the resulting container/endpoints.
public class PassengerHostCompositionTests : IDisposable
{
    private const string ConsumerQueue = "passenger-register-new-user-handler";
    private readonly List<WebApplication> _apps = [];
    private readonly Dictionary<WebApplication, List<ServiceDescriptor>> _registrations = [];

    [Fact]
    public void readiness_checks_cover_the_service_owned_stores_and_the_bus()
    {
        var app = BuildHost();

        var registrations = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations
            .Where(r => r.Tags.Contains(PassengerServiceHostExtensions.ReadyTag))
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(
                "passenger-postgres",
                "passenger-persist-message-postgres",
                "passenger-mongo",
                "masstransit-bus"
            );
        registrations
            .Where(r => r.Tags.Contains("live"))
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(["self"], "the liveness probe must not depend on any backing store");
    }

    [Fact]
    public async Task liveness_probe_is_healthy_without_any_backing_store()
    {
        var app = BuildHost();

        var report = await app
            .Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains("live"));

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Keys.Should().BeEquivalentTo("self");
    }

    [Fact]
    public async Task passenger_postgres_readiness_prefers_the_aspire_connection_string()
    {
        var app = BuildHost(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:passenger"] =
                    "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=aspire;Timeout=1",
                ["PostgresOptions:ConnectionString:Passenger"] =
                    "Host=127.0.0.1;Port=2;Username=u;Password=p;Database=options;Timeout=1",
            }
        );

        var entry = await RunReadinessCheck(app, "passenger-postgres");

        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Exception!.Message.Should().Contain("127.0.0.1:1");
    }

    [Fact]
    public async Task passenger_postgres_readiness_falls_back_to_postgres_options()
    {
        var app = BuildHost(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Passenger"] =
                    "Host=127.0.0.1;Port=2;Username=u;Password=p;Database=options;Timeout=1",
            }
        );

        var entry = await RunReadinessCheck(app, "passenger-postgres");

        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Exception!.Message.Should().Contain("127.0.0.1:2");
    }

    [Fact]
    public async Task passenger_postgres_readiness_reports_a_missing_connection_string()
    {
        var app = BuildHost();

        var act = () => RunReadinessCheck(app, "passenger-postgres");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*Passenger Postgres connection string is not configured*"
        );
    }

    [Fact]
    public async Task persist_message_readiness_uses_the_passenger_outbox_store()
    {
        var app = BuildHost(
            new Dictionary<string, string?>
            {
                ["PersistMessageOptions:ConnectionString:Passenger"] =
                    "Host=127.0.0.1;Port=3;Username=u;Password=p;Database=outbox;Timeout=1",
            }
        );

        var entry = await RunReadinessCheck(app, "passenger-persist-message-postgres");

        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Exception!.Message.Should().Contain("127.0.0.1:3");
    }

    [Theory]
    [InlineData(true, "127.0.0.1:1")]
    [InlineData(false, "127.0.0.1:2")]
    public async Task mongo_readiness_probes_the_passenger_module_read_store(
        bool moduleConnectionStringConfigured,
        string expectedEndpoint
    )
    {
        const string timeouts = "connectTimeoutMS=500&serverSelectionTimeoutMS=500&directConnection=true";
        var settings = new Dictionary<string, string?>
        {
            ["MongoOptions:ConnectionString"] = $"mongodb://127.0.0.1:2/?{timeouts}",
        };
        if (moduleConnectionStringConfigured)
        {
            settings["MongoOptions:Passenger:ConnectionString"] = $"mongodb://127.0.0.1:1/?{timeouts}";
        }
        var app = BuildHost(settings);

        var entry = await RunReadinessCheck(app, "passenger-mongo");

        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Exception!.Message.Should().Contain(expectedEndpoint);
    }

    [Fact]
    public void grpc_health_service_reports_the_passenger_service()
    {
        var app = BuildHost();

        var mappings = app.Services.GetRequiredService<IOptions<GrpcHealthChecksOptions>>().Value.Services;

        mappings.Select(m => m.Name).Should().BeEquivalentTo(string.Empty, "passenger.PassengerGrpcService");
    }

    [Fact]
    public void host_maps_the_root_probe_and_grpc_health_endpoints()
    {
        var app = BuildHost();

        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        routes
            .Should()
            .Contain(
                [
                    "/",
                    PassengerServiceHostExtensions.LivenessEndpointPath,
                    PassengerServiceHostExtensions.ReadinessEndpointPath,
                    "/grpc.health.v1.Health/Check",
                    "/grpc.health.v1.Health/Watch",
                ]
            );
    }

    [Fact]
    public void api_behavior_suppresses_the_model_state_invalid_filter()
    {
        var app = BuildHost();

        app.Services.GetRequiredService<IOptions<ApiBehaviorOptions>>()
            .Value.SuppressModelStateInvalidFilter.Should()
            .BeTrue();
    }

    [Fact]
    public void grpc_services_register_the_exception_interceptor()
    {
        var app = BuildHost();

        app.Services.GetRequiredService<IOptions<GrpcServiceOptions>>()
            .Value.Interceptors.Should()
            .Contain(interceptor => interceptor.Type == typeof(GrpcExceptionInterceptor));
    }

    [Fact]
    public void current_user_provider_is_scoped()
    {
        var app = BuildHost();

        _registrations[app]
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(ICurrentUserProvider)
                && descriptor.ImplementationType == typeof(CurrentUserProvider)
                && descriptor.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("test", false)]
    public void openapi_routes_are_mapped_only_in_development(string environmentName, bool expected)
    {
        var app = BuildHost(environmentName: environmentName);
        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        if (expected)
        {
            routes.Should().Contain("/openapi/{documentName}.json");
        }
        else
        {
            routes.Should().NotContain("/openapi/{documentName}.json");
        }
    }

    [Fact]
    public async Task root_endpoint_returns_the_configured_service_name()
    {
        var app = BuildHost(useTestServer: true);

        await app.StartAsync();
        using var client = app.GetTestClient();

        (await client.GetStringAsync("/")).Should().Be("Passenger-Service");
    }

    [Fact]
    public void mass_transit_registers_only_passenger_consumers_under_the_passenger_queue_prefix()
    {
        var app = BuildHost();

        var consumerTypes = _registrations[app]
            .Select(d => d.ServiceType)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IConsumer).IsAssignableFrom(t))
            .Distinct()
            .ToList();

        consumerTypes.Should().Contain(typeof(RegisterNewUserHandler));
        consumerTypes.Should().OnlyContain(t => t.Assembly == typeof(PassengerRoot).Assembly);

        var formatter = app.Services.GetRequiredService<IEndpointNameFormatter>();
        formatter.Consumer<RegisterNewUserHandler>().Should().Be(ConsumerQueue);
    }

    [Fact]
    public void inbox_for_the_passenger_queue_resolves_the_passenger_module_processor()
    {
        var app = BuildHost();
        var resolver = app.Services.GetRequiredService<InboxStoreResolver>();

        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerRoot>>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IPersistMessageProcessor<PassengerRoot>)).Returns(passengerProcessor);

        var resolved = resolver.Resolve(serviceProvider, new Uri($"rabbitmq://localhost/{ConsumerQueue}"));

        resolved.Should().BeSameAs(passengerProcessor);
        serviceProvider.DidNotReceive().GetService(typeof(IPersistMessageProcessor));
    }

    private static async Task<HealthReportEntry> RunReadinessCheck(WebApplication app, string name)
    {
        var report = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == name);

        return report.Entries.Should().ContainKey(name).WhoseValue;
    }

    private WebApplication BuildHost(
        IDictionary<string, string?>? settings = null,
        string environmentName = "test",
        bool useTestServer = false
    )
    {
        var contentRoot = Directory.CreateTempSubdirectory("passenger-host-test").FullName;
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environmentName, ContentRootPath = contentRoot }
        );
        if (useTestServer)
        {
            builder.WebHost.UseTestServer();
        }

        var defaults = new Dictionary<string, string?>
        {
            ["AppOptions:Name"] = "Passenger-Service",
            ["HealthOptions:Enabled"] = "false",
            ["HealthOptions:DependencyChecksEnabled"] = "false",
            ["MessageBroker:TransportType"] = nameof(TransportType.InMemory),
            ["MessageBroker:ServiceName"] = "passenger",
            ["MessageBroker:UseInbox"] = "true",
            ["MongoOptions:ConnectionString"] = "mongodb://localhost:1",
            ["MongoOptions:Passenger:DatabaseName"] = "passenger_service_read",
            ["Jwt:Authority"] = "https://localhost:3000",
            ["Jwt:Audience"] = "booking-modular-monolith",
            ["ObservabilityOptions:InstrumentationName"] = "passenger_service",
            ["ObservabilityOptions:ServiceName"] = "Passenger Service",
            ["ObservabilityOptions:UsePrometheusExporter"] = "false",
            ["ObservabilityOptions:UseOTLPExporter"] = "false",
            ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            defaults[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(defaults);

        builder.AddPassengerServiceHost();
        builder.AddPassengerModules();

        var app = builder.Build();
        app.UsePassengerServiceHost();

        _apps.Add(app);
        _registrations[app] = [.. builder.Services];
        return app;
    }

    public void Dispose()
    {
        foreach (var app in _apps)
        {
            ((IDisposable)app).Dispose();
        }
    }
}
