using Booking.Extensions.Infrastructure;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;
using GetByIdRequest = Contracts.Grpc.Flight.GetByIdRequest;

namespace Unit.Test.GrpcClient;

public class GrpcClientRegistrationTests
{
    [Fact]
    public async Task flight_call_crosses_network_and_retries_unavailable()
    {
        await using var server = await GrpcTestServer.StartAsync();
        server.State.FailuresBeforeSuccess = 2;
        await using var provider = BuildProvider(server.Address);

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var result = await client.GetByIdAsync(new GetByIdRequest { Id = "42" });

        result.FlightDto.Id.Should().Be("42");
        server.State.GetByIdCalls.Should().Be(3);
    }

    [Fact]
    public async Task non_transient_errors_are_not_retried()
    {
        await using var server = await GrpcTestServer.StartAsync();
        server.State.FailuresBeforeSuccess = 5;
        server.State.FailureStatus = StatusCode.NotFound;
        await using var provider = BuildProvider(server.Address);

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "42" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
        server.State.GetByIdCalls.Should().Be(1);
    }

    [Fact]
    public async Task non_idempotent_reserve_seat_is_not_retried()
    {
        await using var server = await GrpcTestServer.StartAsync();
        await using var provider = BuildProvider(server.Address);

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var act = async () =>
            await client.ReserveSeatAsync(new ReserveSeatRequest { FlightId = "1", SeatNumber = "1A" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.State.ReserveSeatCalls.Should().Be(1);
    }

    [Fact]
    public async Task configured_deadline_is_sent_and_enforced()
    {
        await using var server = await GrpcTestServer.StartAsync();
        await using var provider = BuildProvider(server.Address, new() { ["Grpc:Deadline"] = "00:00:00.500" });
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var before = DateTime.UtcNow;
        await client.GetByIdAsync(new GetByIdRequest { Id = "1" });
        server.State.LastDeadline.Should().BeCloseTo(before.AddMilliseconds(500), TimeSpan.FromMilliseconds(400));

        server.State.Delay = TimeSpan.FromSeconds(5);
        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "2" });
        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public async Task explicit_call_deadline_overrides_configured_default()
    {
        await using var server = await GrpcTestServer.StartAsync();
        await using var provider = BuildProvider(server.Address, new() { ["Grpc:Deadline"] = "00:00:00.200" });
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        server.State.Delay = TimeSpan.FromMilliseconds(600);

        var result = await client.GetByIdAsync(
            new GetByIdRequest { Id = "7" },
            deadline: DateTime.UtcNow.AddSeconds(5)
        );

        result.FlightDto.Id.Should().Be("7");
    }

    [Fact]
    public async Task logical_address_is_resolved_through_service_discovery()
    {
        await using var server = await GrpcTestServer.StartAsync();
        await using var provider = BuildProvider(
            "http://flight",
            new() { ["services:flight:http:0"] = server.Address, ["Grpc:PassengerAddress"] = "http://passenger" }
        );

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var result = await client.GetByIdAsync(new GetByIdRequest { Id = "sd" });

        result.FlightDto.Id.Should().Be("sd");
    }

    [Fact]
    public async Task passenger_client_uses_shared_contract()
    {
        await using var server = await GrpcTestServer.StartAsync();
        await using var provider = BuildProvider(server.Address);

        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();
        var result = await client.GetByIdAsync(new Contracts.Grpc.Passenger.GetByIdRequest { Id = "p1" });

        result.PassengerDto.Name.Should().Be("Test Passenger");
    }

    [Fact]
    public async Task https_address_uses_tls()
    {
        await using var server = await GrpcTestServer.StartAsync(useTls: true);
        server.Address.Should().StartWith("https://");

        await using var trusting = BuildProvider(
            server.Address,
            new() { ["Grpc:AllowUntrustedServerCertificate"] = "true" }
        );
        var result = await trusting
            .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new GetByIdRequest { Id = "tls" });
        result.FlightDto.Id.Should().Be("tls");

        await using var strict = BuildProvider(server.Address, new() { ["Grpc:MaxAttempts"] = "1" });
        var act = async () =>
            await strict
                .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
                .GetByIdAsync(new GetByIdRequest { Id = "tls" });
        (await act.Should().ThrowAsync<RpcException>()).Which.Status.Detail.Should().Contain("SSL");
    }

    [Fact]
    public async Task readiness_checks_report_dependency_health()
    {
        await using var server = await GrpcTestServer.StartAsync();
        await using var healthy = BuildProvider(server.Address);

        var report = await healthy
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains(GrpcClientExtensions.ReadinessTag));

        report
            .Status.Should()
            .Be(
                HealthStatus.Healthy,
                string.Join("; ", report.Entries.Select(e => $"{e.Key}: {e.Value.Description} {e.Value.Exception}"))
            );
        report.Entries.Keys.Should().BeEquivalentTo("flight-grpc", "passenger-grpc");

        await using var unreachable = BuildProvider(
            "http://127.0.0.1:1",
            new() { ["Grpc:MaxAttempts"] = "1", ["Grpc:Deadline"] = "00:00:02" }
        );
        var downReport = await unreachable
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains(GrpcClientExtensions.ReadinessTag));

        downReport.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void missing_address_fails_fast()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Grpc:PassengerAddress"] = "http://passenger" })
            .Build();

        var act = () => new ServiceCollection().AddGrpcClients(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Grpc:FlightAddress*");
    }

    [Fact]
    public void aspire_https_plus_http_scheme_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:FlightAddress"] = "https+http://flight",
                    ["Grpc:PassengerAddress"] = "http://passenger",
                }
            )
            .Build();

        var act = () => new ServiceCollection().AddGrpcClients(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*plain http(s)*");
    }

    // Mirrors the host: ServiceDefaults adds the standard resilience handler and service discovery
    // to every HttpClient, which the gRPC registration must override/coexist with.
    private static ServiceProvider BuildProvider(string address, Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Grpc:FlightAddress"] = address,
            ["Grpc:PassengerAddress"] = address,
        };
        foreach (var (key, value) in overrides ?? new())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        services.AddGrpcClients(configuration);

        return services.BuildServiceProvider();
    }
}
