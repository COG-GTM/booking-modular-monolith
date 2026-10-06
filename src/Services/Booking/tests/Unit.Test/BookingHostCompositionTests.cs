using Booking.Data;
using Booking.Host.Extensions;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.EventStoreDB.BackgroundWorkers;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using Contracts.Grpc.Flight.V1;
using Contracts.Grpc.Passenger.V1;
using EventStore.Client;
using FluentAssertions;
using Grpc.Net.ClientFactory;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using Xunit;

namespace Booking.Host.Unit.Test;

public class BookingHostCompositionTests
{
    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "test",
            }
        );

        builder.AddBookingHost();

        return builder;
    }

    [Fact]
    public void registers_event_sourced_write_model_and_all_stream_subscription()
    {
        var services = CreateBuilder().Services;

        services.Should().Contain(d => d.ServiceType == typeof(EventStoreClient));
        services.Should().Contain(d => d.ServiceType == typeof(IEventStoreDBRepository<>));
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationFactory != null
                && d.ImplementationFactory.Method.ReturnType == typeof(BackgroundWorker)
            );
    }

    [Fact]
    public void registers_booking_owned_mongo_read_model_and_outbox()
    {
        var services = CreateBuilder().Services;

        services.Should().Contain(d => d.ServiceType == typeof(BookingReadDbContext));
        services.Should().Contain(d => d.ServiceType == typeof(PersistMessageDbContext<BookingRoot>));
        services.Should().Contain(d => d.ServiceType == typeof(IPersistMessageProcessor<BookingRoot>));
        services.Should().Contain(d => d.ServiceType == typeof(IEventDispatcher<BookingRoot>));
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>)
            );
        services.Should().Contain(d => d.ServiceType == typeof(IBus));
    }

    [Fact]
    public void binds_booking_stores_from_its_own_configuration()
    {
        using var app = CreateBuilder().Build();

        app.Configuration.GetPostgresConnectionString(nameof(Booking)).Should().Contain("booking");

        var mongoOptions = app.Services.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Booking));
        mongoOptions.DatabaseName.Should().Be("booking_modular_monolith_read");
    }

    [Fact]
    public void creates_flight_and_passenger_grpc_clients_addressed_by_logical_service_name()
    {
        using var app = CreateBuilder().Build();
        var factory = app.Services.GetRequiredService<GrpcClientFactory>();
        var clientOptions = app.Services.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();

        factory.CreateClient<FlightGrpcService.FlightGrpcServiceClient>("flight").Should().NotBeNull();
        factory.CreateClient<PassengerGrpcService.PassengerGrpcServiceClient>("passenger").Should().NotBeNull();

        clientOptions.Get("flight").Address.Should().Be(new Uri("https://flight"));
        clientOptions.Get("passenger").Address.Should().Be(new Uri("https://passenger"));
    }

    [Fact]
    public async Task resolves_flight_and_passenger_endpoints_through_service_discovery()
    {
        using var app = CreateBuilder().Build();
        var resolver = app.Services.GetRequiredService<ServiceEndpointResolver>();

        var flight = await resolver.GetEndpointsAsync("https://flight", CancellationToken.None);
        var passenger = await resolver.GetEndpointsAsync("https://passenger", CancellationToken.None);

        flight.Endpoints.Should().ContainSingle().Which.ToString().Should().Be("https://localhost:3000/");
        passenger.Endpoints.Should().ContainSingle().Which.ToString().Should().Be("https://localhost:3000/");
    }

    [Fact]
    public void registers_readiness_checks_for_owned_stores_and_grpc_dependencies()
    {
        using var app = CreateBuilder().Build();
        var registrations = app
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(r => r.Name);

        registrations
            .Should()
            .Contain(
                [
                    "self",
                    "flight",
                    "passenger",
                    HealthCheckExtensions.PostgresCheckName,
                    HealthCheckExtensions.MongoCheckName,
                    HealthCheckExtensions.EventStoreCheckName,
                    HealthCheckExtensions.RabbitMqCheckName,
                ]
            );
    }

    [Fact]
    public void maps_booking_endpoints_and_health_without_other_modules()
    {
        using var app = CreateBuilder().Build();
        app.UseBookingHost();

        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        routes.Should().Contain("api/v{version:apiVersion}/booking");
        routes.Should().Contain(["/health", "/alive"]);
        routes.Should().NotContain(r => r != null && (r.Contains("/flight") || r.Contains("/passenger") || r.Contains("/identity")));
    }

    [Fact]
    public void does_not_reference_other_modules_or_the_monolith_host()
    {
        var referenced = typeof(Program).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        var loadedModules = AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .Concat(referenced);

        loadedModules.Should().NotContain(["Api", "Flight", "Passenger", "Identity"]);
        referenced.Should().Contain("Booking");
    }
}
