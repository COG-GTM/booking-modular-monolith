using Booking.Booking.Models;
using Booking.Configuration;
using Booking.Data;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Flight;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Passenger;
using Xunit;

namespace Host.Test.Composition;

public sealed class BookingHostCompositionTests : IClassFixture<BookingHostFactory>
{
    private readonly BookingHostFactory _factory;

    public BookingHostCompositionTests(BookingHostFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task maps_only_booking_module_endpoints()
    {
        using var client = _factory.CreateClient();
        var endpoints = _factory
            .Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        endpoints.Should().Contain(endpoint => endpoint!.Contains("/booking", StringComparison.OrdinalIgnoreCase));
        endpoints
            .Should()
            .NotContain(endpoint =>
                endpoint!.Contains("/flight", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains("/passenger", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains("/identity", StringComparison.OrdinalIgnoreCase)
            );

        var response = await client.PostAsync("/api/v1/booking", content: null);
        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task resolves_booking_services_and_discovery_configuration()
    {
        using var client = _factory.CreateClient();
        var services = _factory.Services;

        services.GetRequiredService<IEventDispatcher<global::Booking.BookingRoot>>().Should().NotBeNull();
        services.GetRequiredService<IPersistMessageProcessor<global::Booking.BookingRoot>>().Should().NotBeNull();
        services
            .GetRequiredService<IEventStoreDBRepository<global::Booking.Booking.Models.Booking>>()
            .Should()
            .NotBeNull();
        services.GetRequiredService<BookingReadDbContext>().Should().NotBeNull();
        services.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>().Should().NotBeNull();
        services.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>().Should().NotBeNull();

        var grpcOptions = services.GetRequiredService<IOptions<GrpcOptions>>().Value;
        grpcOptions.Flight.Address.Should().Be("http://_grpc.flight");
        grpcOptions.Passenger.Address.Should().Be("http://_grpc.passenger");
        services.GetRequiredService<IConfiguration>()["Services:flight:grpc:0"].Should().Be("http://127.0.0.1:1");
        services.GetRequiredService<IConfiguration>()["Services:passenger:grpc:0"].Should().Be("http://127.0.0.1:1");
    }

    [Fact]
    public async Task liveness_is_independent_of_dependencies_and_readiness_contains_booking_and_grpc_checks()
    {
        using var client = _factory.CreateClient();
        var live = await client.GetAsync("/health/live");

        live.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var registrations = _factory
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name)
            .ToArray();
        registrations.Should().Contain("booking-postgres");
        registrations.Should().Contain("booking-mongo");
        registrations.Should().Contain("booking-eventstore");
        registrations.Should().Contain("flight-grpc");
        registrations.Should().Contain("passenger-grpc");
        registrations.Should().NotContain("booking-rabbitmq");

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().Contain("booking-postgres");
        body.Should().Contain("booking-mongo");
        body.Should().Contain("booking-eventstore");
        body.Should().Contain("flight-grpc");
        body.Should().Contain("passenger-grpc");
    }

    [Fact]
    public void host_assembly_has_no_forbidden_module_or_aggregate_references()
    {
        var referencedAssemblies = typeof(Booking.Host.Program).Assembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should()
            .NotContain(reference =>
                new[] { "Api", "Flight", "Passenger", "Identity", "BuildingBlocks.EFCore" }.Contains(reference.Name)
            );
    }

    [Theory]
    [InlineData(null, true, "default")]
    [InlineData("true", true, "booking-service")]
    [InlineData("false", false, null)]
    public void background_processing_controls_both_workers_and_subscription_id(
        string? enabled,
        bool expectedEnabled,
        string? expectedSubscriptionId
    )
    {
        var backgroundProcessingEnvironmentVariable = "Modules__Booking__BackgroundProcessingEnabled";
        var previousBackgroundProcessingEnvironmentVariable = Environment.GetEnvironmentVariable(
            backgroundProcessingEnvironmentVariable
        );
        Environment.SetEnvironmentVariable(backgroundProcessingEnvironmentVariable, enabled);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.Sources.Clear();
            var settings = new Dictionary<string, string?>
            {
                ["AppOptions:Name"] = "Booking-Service",
                ["EventStoreOptions:ConnectionString"] = "esdb://localhost:2113?tls=false",
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Booking:DatabaseName"] = "booking_service_read",
                ["PostgresOptions:ConnectionString:Booking"] = "Server=localhost;Database=booking_service",
                ["Grpc:Flight:Address"] = "http://_grpc.flight",
                ["Grpc:Passenger:Address"] = "http://_grpc.passenger",
                ["Services:flight:grpc:0"] = "http://localhost:3002",
                ["Services:passenger:grpc:0"] = "http://localhost:3002",
                ["Modules:Booking:SubscriptionId"] = null,
            };

            if (enabled is not null)
            {
                settings["Modules:Booking:BackgroundProcessingEnabled"] = enabled;
                settings["Modules:Booking:SubscriptionId"] = "booking-service";
            }

            builder.Configuration.AddInMemoryCollection(settings);
            builder.AddBookingModules();
            builder.AddPersistMessageProcessor<global::Booking.BookingRoot>("Booking");

            var hostedServices = builder
                .Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                .ToArray();
            var persistWorker = hostedServices.SingleOrDefault(descriptor =>
                descriptor.ImplementationType?.IsGenericType == true
                && descriptor.ImplementationType.GetGenericTypeDefinition() == typeof(PersistMessageBackgroundService<>)
            );
            var subscriptionOptions = builder.Services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(EventStoreDBSubscriptionToAllOptions)
            );
            var subscriptionWorker = hostedServices.Any(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType is null
                && descriptor.ImplementationFactory is not null
            );

            (persistWorker is not null).Should().Be(expectedEnabled);
            (subscriptionOptions is not null).Should().Be(expectedEnabled);
            subscriptionWorker.Should().Be(expectedEnabled);
            if (expectedEnabled)
            {
                var options = subscriptionOptions!
                    .ImplementationInstance.Should()
                    .BeOfType<EventStoreDBSubscriptionToAllOptions>()
                    .Subject;
                options.SubscriptionId.Should().Be(expectedSubscriptionId);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                backgroundProcessingEnvironmentVariable,
                previousBackgroundProcessingEnvironmentVariable
            );
        }
    }
}
