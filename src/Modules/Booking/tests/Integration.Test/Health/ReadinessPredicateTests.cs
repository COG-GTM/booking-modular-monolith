using Api.Extensions;
using Booking.Extensions.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Integration.Test.Health;

public class ReadinessPredicateTests
{
    [Theory]
    [InlineData("flight-grpc", new[] { GrpcClientExtensions.ReadyHealthTag, GrpcClientExtensions.GrpcDependencyHealthTag }, true)]
    [InlineData("npgsql", new string[0], true)]
    [InlineData("MongoDB-Health", new string[0], true)]
    [InlineData("rabbitmq", new string[0], true)]
    [InlineData("self", new[] { "live" }, false)]
    public void readiness_includes_every_dependency_check_but_not_liveness(string name, string[] tags, bool expected)
    {
        var registration = new HealthCheckRegistration(
            name,
            _ => new DelegateHealthCheck(),
            failureStatus: null,
            tags);

        SharedInfrastructureExtensions.IsReadinessCheck(registration).Should().Be(expected);
    }

    private sealed class DelegateHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy());
    }
}
