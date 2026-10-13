using System.Net;
using Booking.GrpcClient;
using FluentAssertions;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Unit.Test.GrpcClient;

public class GrpcDependencyHealthCheckTests
{
    private const string ServiceName = "flight.FlightGrpcService";
    private const string ClientName = "probe";

    [Fact]
    public async Task serving_dependency_is_healthy()
    {
        await using var server = await HealthProbeServer.StartAsync();
        await using var provider = BuildProvider(server.Address);
        var check = CreateCheck(provider, ServiceName);

        var result = await check.CheckHealthAsync(CreateContext(check, HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be($"{ServiceName} is serving");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task not_serving_dependency_uses_the_registration_failure_status()
    {
        await using var server = await HealthProbeServer.StartAsync();
        server.Healthy = false;
        await using var provider = BuildProvider(server.Address);
        var check = CreateCheck(provider, ServiceName);

        var result = await check.CheckHealthAsync(CreateContext(check, HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain(ServiceName).And.Contain("NotServing");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task unknown_service_name_is_reported_as_a_failed_check()
    {
        await using var server = await HealthProbeServer.StartAsync();
        await using var provider = BuildProvider(server.Address);
        var check = CreateCheck(provider, "flight.v2.FlightGrpcService");

        var result = await check.CheckHealthAsync(CreateContext(check, HealthStatus.Unhealthy));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("health check failed").And.Contain(nameof(StatusCode.NotFound));
        result.Exception.Should().BeOfType<RpcException>().Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task unreachable_dependency_is_reported_with_the_rpc_exception()
    {
        await using var provider = BuildProvider("http://127.0.0.1:1");
        var check = CreateCheck(provider, ServiceName);

        var result = await check.CheckHealthAsync(CreateContext(check, HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeOfType<RpcException>().Which.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public async Task cancellation_propagates_to_the_health_call()
    {
        await using var server = await HealthProbeServer.StartAsync();
        await using var provider = BuildProvider(server.Address);
        var check = CreateCheck(provider, ServiceName);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await check.CheckHealthAsync(CreateContext(check, HealthStatus.Unhealthy), cts.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<RpcException>().Which.StatusCode.Should().Be(StatusCode.Cancelled);
    }

    private static GrpcDependencyHealthCheck CreateCheck(IServiceProvider provider, string serviceName) =>
        new(provider.GetRequiredService<GrpcClientFactory>(), ClientName, serviceName);

    private static HealthCheckContext CreateContext(IHealthCheck check, HealthStatus failureStatus) =>
        new() { Registration = new HealthCheckRegistration("dependency", check, failureStatus, null) };

    private static ServiceProvider BuildProvider(string address)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpcClient<Health.HealthClient>(ClientName, o => o.Address = new Uri(address));
        return services.BuildServiceProvider();
    }

    // Minimal Kestrel host exposing grpc.health.v1 whose reported status can be flipped per test.
    private sealed class HealthProbeServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private HealthProbeServer(WebApplication app)
        {
            _app = app;
            Address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
        }

        public string Address { get; }

        public bool Healthy { get; set; } = true;

        public static async Task<HealthProbeServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel =>
                kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2)
            );

            HealthProbeServer? server = null;
            builder.Services.AddGrpc();
            builder
                .Services.AddHealthChecks()
                .AddCheck(
                    "dependency",
                    () => server!.Healthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("down")
                );
            builder.Services.AddGrpcHealthChecks(options => options.Services.Map(ServiceName, _ => true));

            var app = builder.Build();
            app.MapGrpcHealthChecksService();
            await app.StartAsync();

            server = new HealthProbeServer(app);
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
