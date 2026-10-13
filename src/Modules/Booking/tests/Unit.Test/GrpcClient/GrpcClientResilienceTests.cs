using Booking.Extensions.Infrastructure;
using Contracts.Grpc.Flight;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Xunit;
using GetByIdRequest = Contracts.Grpc.Flight.GetByIdRequest;

namespace Unit.Test.GrpcClient;

// Behaviour of the per-client Polly circuit breaker and the retry attempt cap over a real HTTP/2 hop.
public class GrpcClientResilienceTests
{
    // HttpCircuitBreakerStrategyOptions defaults: MinimumThroughput 100, FailureRatio 0.1.
    private const int CircuitBreakerMinimumThroughput = 100;

    [Fact]
    public async Task retries_stop_at_the_configured_max_attempts()
    {
        await using var server = await GrpcTestServer.StartAsync();
        server.State.FailuresBeforeSuccess = 10;
        await using var provider = BuildProvider(
            server.Address,
            new()
            {
                ["Grpc:MaxAttempts"] = "2",
                ["Grpc:InitialBackoff"] = "00:00:00.010",
                ["Grpc:MaxBackoff"] = "00:00:00.050",
            }
        );

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "42" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.State.GetByIdCalls.Should().Be(2);
    }

    [Fact]
    public async Task circuit_breaker_opens_after_repeated_trailers_only_unavailable_responses()
    {
        await using var server = await GrpcTestServer.StartAsync();
        server.State.FailuresBeforeSuccess = int.MaxValue;
        await using var provider = BuildProvider(server.Address, new() { ["Grpc:MaxAttempts"] = "1" });
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        for (var i = 0; i < CircuitBreakerMinimumThroughput; i++)
        {
            var attempt = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "42" });
            (await attempt.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        }
        server.State.GetByIdCalls.Should().Be(CircuitBreakerMinimumThroughput);

        var rejected = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "42" });
        var exception = (await rejected.Should().ThrowAsync<RpcException>()).Which;

        exception.Status.DebugException.Should().BeAssignableTo<BrokenCircuitException>();
        server
            .State.GetByIdCalls.Should()
            .Be(CircuitBreakerMinimumThroughput, "an open circuit must not hit the server");
    }

    [Fact]
    public async Task non_transient_statuses_do_not_trip_the_circuit_breaker()
    {
        await using var server = await GrpcTestServer.StartAsync();
        server.State.FailuresBeforeSuccess = int.MaxValue;
        server.State.FailureStatus = StatusCode.NotFound;
        await using var provider = BuildProvider(server.Address, new() { ["Grpc:MaxAttempts"] = "1" });
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var calls = CircuitBreakerMinimumThroughput + 10;
        for (var i = 0; i < calls; i++)
        {
            var attempt = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "42" });
            (await attempt.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
        }

        server.State.GetByIdCalls.Should().Be(calls);
    }

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
