using Booking.Extensions.Infrastructure;
using FluentAssertions;
using Flight;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Passenger;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcClientResilienceTests : IAsyncLifetime
{
    private const string UnusedEndpoint = "http://127.0.0.1:9";
    private FakeGrpcServer _server = null!;

    public async Task InitializeAsync() => _server = await FakeGrpcServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task logical_addresses_are_resolved_through_service_discovery()
    {
        await using var sp = GrpcClientTestHost.Build(GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address));

        var flight = await sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new Flight.GetByIdRequest { Id = "f-1" });
        var passenger = await sp.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
            .GetByIdAsync(new Passenger.GetByIdRequest { Id = "p-1" });

        flight.FlightDto.Id.Should().Be("f-1");
        passenger.PassengerDto.Id.Should().Be("p-1");
    }

    [Fact]
    public async Task concrete_dns_addresses_are_used_as_is()
    {
        await using var sp = GrpcClientTestHost.Build(new Dictionary<string, string?>
        {
            ["Grpc:Flight:Address"] = _server.Address,
            ["Grpc:Passenger:Address"] = _server.Address,
        });

        var result = await sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new Flight.GetByIdRequest { Id = "f-2" });

        result.FlightDto.Id.Should().Be("f-2");
    }

    [Fact]
    public async Task idempotent_reads_are_retried_on_unavailable()
    {
        _server.Flight.GetByIdFailures = 2;
        _server.Passenger.GetByIdFailures = 3;
        await using var sp = GrpcClientTestHost.Build(GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address));

        var flight = await sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new Flight.GetByIdRequest { Id = "f-3" });
        var passenger = await sp.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
            .GetByIdAsync(new Passenger.GetByIdRequest { Id = "p-3" });

        flight.FlightDto.Id.Should().Be("f-3");
        passenger.PassengerDto.Id.Should().Be("p-3");
        _server.Flight.GetByIdCalls.Should().Be(3);
        _server.Passenger.GetByIdCalls.Should().Be(4);
    }

    [Fact]
    public async Task retries_are_bounded_by_max_attempts()
    {
        _server.Flight.GetByIdFailures = int.MaxValue;
        var settings = GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address);
        settings["Grpc:Flight:MaxAttempts"] = "3";
        await using var sp = GrpcClientTestHost.Build(settings);

        var call = () => sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new Flight.GetByIdRequest { Id = "f-4" }).ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        _server.Flight.GetByIdCalls.Should().Be(3);
    }

    [Fact]
    public async Task reserve_seat_is_never_retried()
    {
        _server.Flight.ReserveSeatUnavailable = true;
        await using var sp = GrpcClientTestHost.Build(GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address));

        var call = () => sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .ReserveSeatAsync(new ReserveSeatRequest { FlightId = "f-5", SeatNumber = "12A" }).ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        _server.Flight.ReserveSeatCalls.Should().Be(1);
    }

    [Fact]
    public async Task calls_without_a_deadline_get_the_configured_default()
    {
        _server.Flight.HangGetAvailableSeats = true;
        var settings = GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address);
        settings["Grpc:Flight:Deadline"] = "00:00:00.300";
        await using var sp = GrpcClientTestHost.Build(settings);
        var client = sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var started = DateTime.UtcNow;
        var call = () => client.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = "f-6" }).ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));

        await client.GetByIdAsync(new Flight.GetByIdRequest { Id = "f-6" });
        _server.Flight.LastDeadline.Should().NotBeNull().And.BeBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task an_explicit_deadline_is_not_overridden()
    {
        await using var sp = GrpcClientTestHost.Build(GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address));
        var explicitDeadline = DateTime.UtcNow.AddMinutes(5);

        await sp.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new Flight.GetByIdRequest { Id = "f-7" }, deadline: explicitDeadline);

        _server.Flight.LastDeadline.Should().BeCloseTo(explicitDeadline, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task readiness_is_healthy_when_dependencies_serve()
    {
        await using var sp = GrpcClientTestHost.Build(GrpcClientTestHost.DiscoverySettings(_server.Address, _server.Address));

        var report = await sp.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains(GrpcClientExtensions.GrpcDependencyHealthTag));

        report.Entries.Keys.Should().BeEquivalentTo("flight-grpc", "passenger-grpc");
        report.Status.Should().Be(HealthStatus.Healthy, string.Join("; ", report.Entries.Select(e => $"{e.Key}: {e.Value.Description} {e.Value.Exception}")));
        report.Entries.Values.Should().AllSatisfy(e => e.Tags.Should().Contain(GrpcClientExtensions.ReadyHealthTag));
    }

    [Fact]
    public async Task readiness_is_unhealthy_when_a_dependency_is_down()
    {
        await using var sp = GrpcClientTestHost.Build(GrpcClientTestHost.DiscoverySettings(_server.Address, UnusedEndpoint));

        var report = await sp.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains(GrpcClientExtensions.GrpcDependencyHealthTag));

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["flight-grpc"].Status.Should().Be(HealthStatus.Healthy);
        report.Entries["passenger-grpc"].Status.Should().Be(HealthStatus.Unhealthy);
    }
}
