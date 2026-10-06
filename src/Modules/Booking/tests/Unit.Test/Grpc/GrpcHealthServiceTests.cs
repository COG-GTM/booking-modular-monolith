using BuildingBlocks.Grpc;
using FluentAssertions;
using Grpc.AspNetCore.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcHealthServiceTests
{
    [Fact]
    public void should_expose_host_checks_but_exclude_outbound_grpc_dependency_checks()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddGrpcHealthService();

        var provider = services.BuildServiceProvider();
        var mapping = provider
            .GetRequiredService<IOptions<GrpcHealthChecksOptions>>()
            .Value.Services.Should()
            .ContainSingle()
            .Subject;

        mapping.Name.Should().BeEmpty();
        mapping.HealthCheckPredicate.Should().NotBeNull();
        mapping.HealthCheckPredicate!(new HealthCheckMapContext("postgres", ["ready"])).Should().BeTrue();
        mapping.HealthCheckPredicate(new HealthCheckMapContext("self", [])).Should().BeTrue();
        mapping
            .HealthCheckPredicate(
                new HealthCheckMapContext("flight", [Extensions.ReadinessTag, Extensions.GrpcDependencyTag])
            )
            .Should()
            .BeFalse();
    }
}
