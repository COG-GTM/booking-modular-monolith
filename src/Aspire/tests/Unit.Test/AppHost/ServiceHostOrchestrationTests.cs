using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace Unit.Test.AppHost;

public class ServiceHostOrchestrationTests : IClassFixture<AppHostFixture>
{
    private const string IdentityHttpsExpression = "{identity-service.bindings.https.url}";

    private readonly AppHostFixture _fixture;

    public ServiceHostOrchestrationTests(AppHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AppHost_ShouldRegisterAllFourServiceHosts()
    {
        var projectNames = _fixture.Builder.Resources
            .OfType<ProjectResource>()
            .Select(r => r.Name)
            .ToList();

        Assert.Contains("identity-service", projectNames);
        Assert.Contains("flight-service", projectNames);
        Assert.Contains("passenger-service", projectNames);
        Assert.Contains("booking-service", projectNames);
    }

    [Fact]
    public void AppHost_ShouldNotRegisterMonolithApi()
    {
        var projectNames = _fixture.Builder.Resources
            .OfType<ProjectResource>()
            .Select(r => r.Name)
            .ToList();

        Assert.DoesNotContain("api", projectNames);
    }

    [Theory]
    [InlineData("identity-service", new[] { "identity", "persist-message", "rabbitmq" })]
    [InlineData("flight-service", new[] { "flight", "persist-message", "mongo", "rabbitmq" })]
    [InlineData("passenger-service", new[] { "passenger", "persist-message", "mongo", "rabbitmq" })]
    [InlineData(
        "booking-service",
        new[] { "eventstore", "persist-message", "mongo", "rabbitmq", "flight-service", "passenger-service" })]
    public void ServiceHost_ShouldWaitForItsDependencies(string serviceName, string[] expectedDependencies)
    {
        var service = GetProjectResource(serviceName);

        Assert.True(service.TryGetAnnotationsOfType<WaitAnnotation>(out var waitAnnotations));
        var waitedResourceNames = waitAnnotations.Select(w => w.Resource.Name).ToList();

        foreach (var dependency in expectedDependencies)
        {
            Assert.Contains(dependency, waitedResourceNames);
        }
    }

    [Theory]
    [InlineData("identity-service", new[] { "identity", "persist-message", "rabbitmq" })]
    [InlineData("flight-service", new[] { "flight", "persist-message", "mongo", "rabbitmq" })]
    [InlineData("passenger-service", new[] { "passenger", "persist-message", "mongo", "rabbitmq" })]
    [InlineData(
        "booking-service",
        new[] { "eventstore", "persist-message", "mongo", "rabbitmq", "flight-service", "passenger-service" })]
    public void ServiceHost_ShouldReferenceItsDependencies(string serviceName, string[] expectedReferences)
    {
        var service = GetProjectResource(serviceName);

        Assert.True(service.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var relationships));
        var referencedResourceNames = relationships
            .Where(r => r.Type == "Reference")
            .Select(r => r.Resource.Name)
            .ToList();

        foreach (var reference in expectedReferences)
        {
            Assert.Contains(reference, referencedResourceNames);
        }
    }

    [Theory]
    [InlineData("flight-service")]
    [InlineData("passenger-service")]
    [InlineData("booking-service")]
    public async Task ServiceHost_ShouldPointJwtAuthorityAtIdentityService(string serviceName)
    {
        var envValues = await GetEnvironmentVariablesAsync(serviceName);

        Assert.Equal(IdentityHttpsExpression, envValues["Jwt__Authority"]);
    }

    [Fact]
    public async Task IdentityService_ShouldUseItsOwnHttpsEndpointAsIssuerAndAuthority()
    {
        var envValues = await GetEnvironmentVariablesAsync("identity-service");

        Assert.Equal(IdentityHttpsExpression, envValues["AuthOptions__IssuerUri"]);
        Assert.Equal(IdentityHttpsExpression, envValues["Jwt__Authority"]);
    }

    [Fact]
    public async Task BookingService_ShouldWireGrpcClientAddressesToServiceEndpoints()
    {
        var envValues = await GetEnvironmentVariablesAsync("booking-service");

        Assert.Equal("{flight-service.bindings.https.url}", envValues["Grpc__FlightAddress"]);
        Assert.Equal("{passenger-service.bindings.https.url}", envValues["Grpc__PassengerAddress"]);
    }

    private ProjectResource GetProjectResource(string name)
    {
        return _fixture.Builder.Resources
            .OfType<ProjectResource>()
            .Single(r => string.Equals(r.Name, name, StringComparison.Ordinal));
    }

    private async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(string serviceName)
    {
        var service = GetProjectResource(serviceName);
        return await service.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
    }
}
