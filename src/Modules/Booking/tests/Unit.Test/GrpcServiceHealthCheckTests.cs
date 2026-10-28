using System.Collections.Concurrent;
using System.Net;
using Booking.Extensions.Infrastructure;
using FluentAssertions;
using Grpc.Core;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;
using HealthService = Grpc.Health.V1.Health;
using ServingStatus = Grpc.Health.V1.HealthCheckResponse.Types.ServingStatus;

namespace Booking.Unit.Test;

public class GrpcServiceHealthCheckTests
{
    [Fact]
    public async Task health_checks_send_the_expected_services_and_use_a_three_second_deadline()
    {
        await using var server = await TestHealthServer.StartAsync();
        server.Service.Statuses["flight.FlightGrpcService"] = ServingStatus.Serving;
        server.Service.Statuses["passenger.PassengerGrpcService"] = ServingStatus.Serving;
        using var provider = CreateBookingProvider(server.Address);
        var healthChecks = provider.GetRequiredService<HealthCheckService>();
        var before = DateTime.UtcNow;

        var result = await healthChecks.CheckHealthAsync(check => check.Tags.Contains("ready"));

        result.Entries["grpc-flight"].Status.Should().Be(HealthStatus.Healthy);
        result.Entries["grpc-passenger"].Status.Should().Be(HealthStatus.Healthy);
        server.Service.Calls.Should().HaveCount(2);
        server
            .Service.Calls.Select(call => call.Service)
            .Should()
            .BeEquivalentTo("flight.FlightGrpcService", "passenger.PassengerGrpcService");
        foreach (var call in server.Service.Calls)
        {
            call.Deadline.Should().BeAfter(before.AddSeconds(2));
            call.Deadline.Should().BeBefore(before.AddSeconds(4));
        }
    }

    [Fact]
    public async Task not_serving_status_is_reported_as_unhealthy()
    {
        await using var server = await TestHealthServer.StartAsync();
        server.Service.Statuses["flight.FlightGrpcService"] = ServingStatus.NotServing;
        using var provider = CreateBookingProvider(server.Address);
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var result = await healthChecks.CheckHealthAsync(check => check.Name == "grpc-flight");

        result.Entries["grpc-flight"].Status.Should().Be(HealthStatus.Unhealthy);
        result.Entries["grpc-flight"].Description.Should().Be("gRPC health status: NotServing.");
    }

    [Fact]
    public async Task not_found_is_reported_as_unhealthy_with_the_rpc_exception()
    {
        await using var server = await TestHealthServer.StartAsync();
        using var provider = CreateBookingProvider(server.Address);
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var result = await healthChecks.CheckHealthAsync(check => check.Name == "grpc-flight");

        result.Entries["grpc-flight"].Status.Should().Be(HealthStatus.Unhealthy);
        result.Entries["grpc-flight"].Description.Should().Contain("unknown service");
        result.Entries["grpc-flight"].Exception.Should().BeOfType<RpcException>();
    }

    [Fact]
    public async Task serving_status_is_reported_as_healthy()
    {
        await using var server = await TestHealthServer.StartAsync();
        server.Service.Statuses["passenger.PassengerGrpcService"] = ServingStatus.Serving;
        using var provider = CreateBookingProvider(server.Address);
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var result = await healthChecks.CheckHealthAsync(check => check.Name == "grpc-passenger");

        result.Entries["grpc-passenger"].Status.Should().Be(HealthStatus.Healthy);
    }

    private static ServiceProvider CreateBookingProvider(string address)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:FlightAddress"] = address,
                    ["Grpc:PassengerAddress"] = address,
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrpcClients(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class TestHealthServer(WebApplication application, string address) : IAsyncDisposable
    {
        public string Address { get; } = address;
        public RecordingHealthService Service { get; } =
            application.Services.GetRequiredService<RecordingHealthService>();

        public static async Task<TestHealthServer> StartAsync()
        {
            var service = new RecordingHealthService();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2)
            );
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(service);

            var application = builder.Build();
            application.MapGrpcService<RecordingHealthService>();
            await application.StartAsync();

            var addresses = application
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses;
            return new TestHealthServer(application, addresses.Single());
        }

        public ValueTask DisposeAsync() => application.DisposeAsync();
    }

    private sealed class RecordingHealthService : HealthService.HealthBase
    {
        public ConcurrentDictionary<string, ServingStatus> Statuses { get; } = new();
        public ConcurrentQueue<(string Service, DateTime Deadline)> Calls { get; } = new();

        public override Task<HealthCheckResponse> Check(HealthCheckRequest request, ServerCallContext context)
        {
            Calls.Enqueue((request.Service, context.Deadline));
            if (!Statuses.TryGetValue(request.Service, out var status))
            {
                throw new RpcException(new Status(StatusCode.NotFound, "unknown service"));
            }

            return Task.FromResult(new HealthCheckResponse { Status = status });
        }
    }
}
