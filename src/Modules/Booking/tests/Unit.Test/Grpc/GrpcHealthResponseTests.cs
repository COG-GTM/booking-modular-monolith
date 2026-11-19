using Booking.Extensions.Infrastructure;
using FluentAssertions;
using Grpc.Core;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcHealthResponseTests
{
    [Theory]
    [InlineData(HealthCheckResponse.Types.ServingStatus.Unknown)]
    [InlineData(HealthCheckResponse.Types.ServingStatus.NotServing)]
    [InlineData(HealthCheckResponse.Types.ServingStatus.ServiceUnknown)]
    public async Task non_serving_responses_make_readiness_unhealthy(HealthCheckResponse.Types.ServingStatus status)
    {
        var service = new ProbeService { Status = status };
        await using var app = await StartAsync(service);
        await using var provider = GrpcClientTestHost.Build(Settings(app));

        var report = await CheckAsync(provider);

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report
            .Entries.Values.Should()
            .AllSatisfy(entry =>
            {
                entry.Status.Should().Be(HealthStatus.Unhealthy);
                entry.Description.Should().Be($"Dependency reports {status}.");
            });
        service.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.PermissionDenied)]
    public async Task rpc_errors_report_status_without_retrying_health_probes(StatusCode error)
    {
        var service = new ProbeService { Error = error };
        await using var app = await StartAsync(service);
        await using var provider = GrpcClientTestHost.Build(Settings(app));

        var report = await CheckAsync(provider);

        report
            .Entries.Values.Should()
            .AllSatisfy(entry =>
            {
                entry.Status.Should().Be(HealthStatus.Unhealthy);
                entry.Description.Should().Be($"gRPC health call failed: {error}.");
            });
        service.Calls.Should().Be(2);
    }

    [Fact]
    public async Task a_hung_health_service_is_bounded_by_its_own_timeout()
    {
        var service = new ProbeService { Hang = true };
        await using var app = await StartAsync(service);
        var settings = Settings(app);
        foreach (var client in new[] { "Flight", "Passenger" })
        {
            settings[$"Grpc:{client}:HealthCheckTimeout"] = "00:00:01";
            settings[$"Grpc:{client}:Deadline"] = "00:01:00";
        }
        await using var provider = GrpcClientTestHost.Build(settings);

        var report = await CheckAsync(provider).WaitAsync(TimeSpan.FromSeconds(10));

        report
            .Entries.Values.Should()
            .AllSatisfy(entry =>
            {
                entry.Status.Should().Be(HealthStatus.Unhealthy);
                entry.Description.Should().Be("gRPC health call failed: DeadlineExceeded.");
            });
        service.Calls.Should().Be(2);
    }

    private static Task<HealthReport> CheckAsync(ServiceProvider provider) =>
        provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains(GrpcClientExtensions.GrpcDependencyHealthTag));

    private static Dictionary<string, string?> Settings(WebApplication app)
    {
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        return GrpcClientTestHost.DiscoverySettings(address, address);
    }

    private static async Task<WebApplication> StartAsync(ProbeService service)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2)
        );
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapGrpcService<ProbeService>();
        await app.StartAsync();
        return app;
    }

    public sealed class ProbeService : Health.HealthBase
    {
        private int _calls;

        public HealthCheckResponse.Types.ServingStatus Status { get; init; } =
            HealthCheckResponse.Types.ServingStatus.Serving;
        public StatusCode? Error { get; init; }
        public bool Hang { get; init; }
        public int Calls => _calls;

        public override async Task<HealthCheckResponse> Check(HealthCheckRequest request, ServerCallContext context)
        {
            Interlocked.Increment(ref _calls);
            if (Error is { } error)
                throw new RpcException(new Status(error, "probe failure"));
            if (Hang)
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return new HealthCheckResponse { Status = Status };
        }
    }
}
