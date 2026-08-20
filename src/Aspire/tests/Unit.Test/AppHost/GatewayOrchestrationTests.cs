using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace Unit.Test.AppHost;

public class GatewayOrchestrationTests : IClassFixture<AppHostFixture>
{
    private static readonly string[] ServiceNames =
    [
        "flight-service",
        "passenger-service",
        "identity-service",
        "booking-service",
    ];

    private readonly AppHostFixture _fixture;

    public GatewayOrchestrationTests(AppHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AppHost_ShouldRegisterGateway()
    {
        var projectNames = _fixture.Builder.Resources
            .OfType<ProjectResource>()
            .Select(r => r.Name)
            .ToList();

        Assert.Contains("gateway", projectNames);
    }

    [Fact]
    public void Gateway_ShouldWaitForAllFourServiceHosts()
    {
        var gateway = GetGatewayResource();

        Assert.True(gateway.TryGetAnnotationsOfType<WaitAnnotation>(out var waitAnnotations));
        var waitedResourceNames = waitAnnotations.Select(w => w.Resource.Name).ToList();

        foreach (var serviceName in ServiceNames)
        {
            Assert.Contains(serviceName, waitedResourceNames);
        }
    }

    [Fact]
    public void Gateway_ShouldReferenceAllFourServiceHosts()
    {
        var gateway = GetGatewayResource();

        Assert.True(gateway.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var relationships));
        var referencedResourceNames = relationships
            .Where(r => r.Type == "Reference")
            .Select(r => r.Resource.Name)
            .ToList();

        foreach (var serviceName in ServiceNames)
        {
            Assert.Contains(serviceName, referencedResourceNames);
        }
    }

    [Theory]
    [InlineData("flight", "flight-service")]
    [InlineData("passenger", "passenger-service")]
    [InlineData("identity", "identity-service")]
    [InlineData("booking", "booking-service")]
    public async Task Gateway_ShouldOverrideClusterDestinationWithServiceEndpoint(
        string clusterName,
        string serviceName)
    {
        var envValues = await GetEnvironmentVariablesAsync("gateway");

        var key =
            $"ReverseProxy__Clusters__{clusterName}-cluster__Destinations__{clusterName}-destination__Address";
        Assert.Equal($"{{{serviceName}.bindings.https.url}}", envValues[key]);
    }

    private ProjectResource GetGatewayResource()
    {
        return _fixture.Builder.Resources
            .OfType<ProjectResource>()
            .Single(r => string.Equals(r.Name, "gateway", StringComparison.Ordinal));
    }

    private async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string serviceName)
    {
        var service = _fixture.Builder.Resources
            .OfType<ProjectResource>()
            .Single(r => string.Equals(r.Name, serviceName, StringComparison.Ordinal));
        return await service.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
    }
}
