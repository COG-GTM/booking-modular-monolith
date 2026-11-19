using Flight;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcPolicyBoundaryTests
{
    [Fact]
    public async Task one_attempt_disables_retries_for_both_clients()
    {
        await using var server = await FakeGrpcServer.StartAsync();
        server.Flight.GetByIdFailures = 1;
        server.Passenger.GetByIdFailures = 1;
        var settings = GrpcClientTestHost.DiscoverySettings(server.Address, server.Address);
        settings["Grpc:Flight:MaxAttempts"] = "1";
        settings["Grpc:Passenger:MaxAttempts"] = "1";
        await using var provider = GrpcClientTestHost.Build(settings);

        var flight = () =>
            provider
                .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
                .GetByIdAsync(new Flight.GetByIdRequest())
                .ResponseAsync;
        var passenger = () =>
            provider
                .GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
                .GetByIdAsync(new Passenger.GetByIdRequest())
                .ResponseAsync;

        (await flight.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        (await passenger.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.Flight.GetByIdCalls.Should().Be(1);
        server.Passenger.GetByIdCalls.Should().Be(1);
    }

    [Fact]
    public async Task retry_limits_are_independent_for_each_client()
    {
        await using var server = await FakeGrpcServer.StartAsync();
        server.Flight.GetByIdFailures = int.MaxValue;
        server.Passenger.GetByIdFailures = int.MaxValue;
        var settings = GrpcClientTestHost.DiscoverySettings(server.Address, server.Address);
        settings["Grpc:Flight:MaxAttempts"] = "2";
        settings["Grpc:Passenger:MaxAttempts"] = "5";
        await using var provider = GrpcClientTestHost.Build(settings);

        var flight = () =>
            provider
                .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
                .GetByIdAsync(new Flight.GetByIdRequest())
                .ResponseAsync;
        var passenger = () =>
            provider
                .GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
                .GetByIdAsync(new Passenger.GetByIdRequest())
                .ResponseAsync;

        (await flight.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        (await passenger.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.Flight.GetByIdCalls.Should().Be(2);
        server.Passenger.GetByIdCalls.Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task blocking_calls_apply_defaults_but_preserve_explicit_deadlines(bool explicitDeadline)
    {
        await using var server = await FakeGrpcServer.StartAsync();
        var settings = GrpcClientTestHost.DiscoverySettings(server.Address, server.Address);
        settings["Grpc:Flight:Deadline"] = "00:00:20";
        await using var provider = GrpcClientTestHost.Build(settings);
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var before = DateTime.UtcNow;
        DateTime? deadline = explicitDeadline ? before.AddMinutes(5) : null;

        var response = client.GetById(new Flight.GetByIdRequest { Id = "blocking-flight" }, deadline: deadline);

        response.FlightDto.Id.Should().Be("blocking-flight");
        server.Flight.LastDeadline.Should().NotBeNull();
        server.Flight.LastDeadline!.Value.Should().BeOnOrAfter(before.AddSeconds(explicitDeadline ? 299 : 19));
        server.Flight.LastDeadline.Value.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(explicitDeadline ? 301 : 21));
    }

    [Fact]
    public async Task default_deadline_does_not_replace_caller_cancellation()
    {
        await using var server = await FakeGrpcServer.StartAsync();
        var settings = GrpcClientTestHost.DiscoverySettings(server.Address, server.Address);
        await using var provider = GrpcClientTestHost.Build(settings);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var call = () =>
            provider
                .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
                .GetByIdAsync(new Flight.GetByIdRequest(), cancellationToken: cancellation.Token)
                .ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Cancelled);
        server.Flight.GetByIdCalls.Should().Be(0);
    }
}
