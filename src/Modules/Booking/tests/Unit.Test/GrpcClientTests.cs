using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Booking.Extensions.Infrastructure;
using Contracts.Grpc.Flight;
using FluentAssertions;
using Grpc.AspNetCore.HealthChecks;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using FlightGrpcService = Contracts.Grpc.Flight.FlightGrpcService;

namespace Booking.Unit.Test;

public class GrpcClientTests
{
    [Fact]
    public void clients_use_configured_addresses_and_reject_invalid_addresses()
    {
        var address = "https://flight.example:443";
        using var provider = CreateClientProvider(address, "http://passenger.example:81");
        provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>().Should().NotBeNull();
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
        options.Get(typeof(FlightGrpcService.FlightGrpcServiceClient).Name).Address.Should().Be(new Uri(address));

        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateClientProvider(null, "http://passenger.example:81")
        );
        exception.Message.Should().Contain("Grpc:FlightAddress");
        AssertInvalidAddress("relative/path");
        AssertInvalidAddress("ftp://flight.example");
    }

    [Fact]
    public async Task clients_retry_reads_once_per_attempt_and_never_retry_reservations()
    {
        await using var server = await TestGrpcServer.StartAsync();
        using var provider = CreateClientProvider(
            server.Address,
            server.Address,
            timeout: TimeSpan.FromSeconds(5),
            addStandardResilience: true
        );
        var flightClient = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var response = await flightClient.GetByIdAsync(new GetByIdRequest { Id = "retry" });
        response.Should().NotBeNull();
        server.Flight.GetByIdCalls.Should().Be(3);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () =>
                flightClient
                    .ReserveSeatAsync(new ReserveSeatRequest { FlightId = "flight", SeatNumber = "1A" })
                    .ResponseAsync
        );
        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        server.Flight.ReserveSeatCalls.Should().Be(1);
    }

    [Fact]
    public async Task max_retry_attempts_of_one_disables_retries_and_healthy_calls_succeed()
    {
        await using var server = await TestGrpcServer.StartAsync();
        using var provider = CreateClientProvider(server.Address, server.Address, maxRetryAttempts: 1);
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.GetByIdAsync(new GetByIdRequest { Id = "retry" }).ResponseAsync
        );
        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        server.Flight.GetByIdCalls.Should().Be(1);

        var response = await client.GetByIdAsync(new GetByIdRequest { Id = "healthy" });
        response.Should().NotBeNull();
        server.Flight.GetByIdCalls.Should().Be(2);
    }

    [Fact]
    public async Task calls_apply_default_deadline_preserve_explicit_deadline_and_time_out_slow_handlers()
    {
        await using var server = await TestGrpcServer.StartAsync();
        using var provider = CreateClientProvider(server.Address, server.Address);
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var before = DateTime.UtcNow;
        await client.GetByIdAsync(new GetByIdRequest { Id = "deadline" });
        server.Flight.Deadlines.Last().Should().BeAfter(before.AddSeconds(9));
        server.Flight.Deadlines.Last().Should().BeBefore(before.AddSeconds(11));

        var explicitDeadline = DateTime.UtcNow.AddSeconds(2);
        await client.GetByIdAsync(new GetByIdRequest { Id = "deadline" }, deadline: explicitDeadline);
        server.Flight.Deadlines.Last().Should().BeCloseTo(explicitDeadline, TimeSpan.FromMilliseconds(100));

        using var shortDeadlineProvider = CreateClientProvider(
            server.Address,
            server.Address,
            timeout: TimeSpan.FromMilliseconds(300)
        );
        var shortDeadlineClient = shortDeadlineProvider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var exception = await Assert.ThrowsAsync<RpcException>(
            () => shortDeadlineClient.GetByIdAsync(new GetByIdRequest { Id = "slow" }).ResponseAsync
        );
        exception.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
    }

    [Fact]
    public async Task health_checks_report_serving_and_unavailable_services()
    {
        await using var server = await TestGrpcServer.StartAsync();
        using var provider = CreateClientProvider(server.Address, server.Address);
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var healthy = await healthChecks.CheckHealthAsync(check => check.Tags.Contains("ready"));
        healthy.Entries["grpc-flight"].Status.Should().Be(HealthStatus.Healthy);
        healthy.Entries["grpc-passenger"].Status.Should().Be(HealthStatus.Healthy);

        await server.DisposeAsync();
        var unhealthy = await healthChecks.CheckHealthAsync(check => check.Name == "grpc-flight");
        unhealthy.Entries["grpc-flight"].Status.Should().Be(HealthStatus.Unhealthy);
    }

    private static ServiceProvider CreateClientProvider(
        string? flightAddress,
        string? passengerAddress,
        TimeSpan? timeout = null,
        bool addStandardResilience = false,
        int? maxRetryAttempts = null
    )
    {
        var settings = new Dictionary<string, string?>();
        if (flightAddress is not null)
        {
            settings["Grpc:FlightAddress"] = flightAddress;
        }
        if (passengerAddress is not null)
        {
            settings["Grpc:PassengerAddress"] = passengerAddress;
        }
        if (timeout.HasValue)
        {
            settings["Grpc:Deadline"] = timeout.Value.ToString();
        }
        if (maxRetryAttempts.HasValue)
        {
            settings["Grpc:MaxRetryAttempts"] = maxRetryAttempts.Value.ToString();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        if (addStandardResilience)
        {
            services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrpcClients(configuration);
        return services.BuildServiceProvider();
    }

    private static void AssertInvalidAddress(string address)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateClientProvider(address, "http://passenger.example:81")
        );
        exception.Message.Should().Contain("Grpc:FlightAddress");
    }

    private sealed class TestGrpcServer : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private bool _disposed;

        private TestGrpcServer(WebApplication application, string address, TestFlightService flight)
        {
            _application = application;
            Address = address;
            Flight = flight;
        }

        public string Address { get; }
        public TestFlightService Flight { get; }

        public static async Task<TestGrpcServer> StartAsync()
        {
            var flight = new TestFlightService();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2)
            );
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(flight);
            builder.Services.AddSingleton<TestPassengerService>();
            builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
            builder.Services.AddGrpcHealthChecks(options =>
            {
                options.Services.Map("", context => context.Tags.Contains("live"));
                options.Services.Map("flight.FlightGrpcService", context => context.Tags.Contains("live"));
                options.Services.Map("passenger.PassengerGrpcService", context => context.Tags.Contains("live"));
            });
            var application = builder.Build();
            application.MapGrpcService<TestFlightService>();
            application.MapGrpcService<TestPassengerService>();
            application.MapGrpcHealthChecksService();
            await application.StartAsync();
            var addresses = application
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses;
            return new TestGrpcServer(application, addresses.Single(), flight);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await _application.DisposeAsync();
            }
        }
    }

    private sealed class TestFlightService : FlightGrpcService.FlightGrpcServiceBase
    {
        private int _getByIdCalls;
        private int _reserveSeatCalls;

        public int GetByIdCalls => _getByIdCalls;
        public int ReserveSeatCalls => _reserveSeatCalls;
        public ConcurrentQueue<DateTime> Deadlines { get; } = new();

        public override async Task<GetFlightByIdResult> GetById(GetByIdRequest request, ServerCallContext context)
        {
            Deadlines.Enqueue(context.Deadline);
            var call = Interlocked.Increment(ref _getByIdCalls);
            if (request.Id == "slow")
            {
                await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
            }
            if (request.Id == "retry" && call <= 2)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "retry"));
            }
            return new GetFlightByIdResult();
        }

        public override Task<ReserveSeatResult> ReserveSeat(ReserveSeatRequest request, ServerCallContext context)
        {
            Interlocked.Increment(ref _reserveSeatCalls);
            throw new RpcException(new Status(StatusCode.Unavailable, "not available"));
        }
    }

    private sealed class TestPassengerService : Contracts.Grpc.Passenger.PassengerGrpcService.PassengerGrpcServiceBase
    {
        public override Task<Contracts.Grpc.Passenger.GetPassengerByIdResult> GetById(
            Contracts.Grpc.Passenger.GetByIdRequest request,
            ServerCallContext context
        )
        {
            return Task.FromResult(new Contracts.Grpc.Passenger.GetPassengerByIdResult());
        }
    }
}
