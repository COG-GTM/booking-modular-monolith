using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Xunit;

namespace AppHost.Unit.Test;

public sealed class AppHostFixture : IAsyncLifetime
{
    public IDistributedApplicationTestingBuilder Builder { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
    }

    public async Task DisposeAsync()
    {
        if (Builder is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }
}

public class AppHostResourceGraphTests : IClassFixture<AppHostFixture>
{
    private static readonly string[] ServiceNames =
    [
        "identity-service",
        "flight-service",
        "passenger-service",
        "booking-service",
    ];

    private readonly AppHostFixture _fixture;

    public AppHostResourceGraphTests(AppHostFixture fixture)
    {
        _fixture = fixture;
    }

    private IResourceCollection Resources => _fixture.Builder.Resources;

    private IResource GetResource(string name) =>
        Resources.Should().ContainSingle(r => r.Name == name).Subject;

    private ProjectResource GetProject(string name) =>
        Resources.OfType<ProjectResource>().Should().ContainSingle(r => r.Name == name).Subject;

    [Fact]
    public void apphost_should_register_the_four_service_hosts_and_no_api_resource()
    {
        var projectNames = Resources.OfType<ProjectResource>().Select(r => r.Name).ToList();

        projectNames.Should().BeEquivalentTo([.. ServiceNames, "gateway"]);
        Resources.Should().NotContain(r => r.Name == "api");
    }

    [Fact]
    public void apphost_should_register_one_outbox_database_per_service()
    {
        var databases = Resources.OfType<PostgresDatabaseResource>()
            .ToDictionary(db => db.Name, db => db.DatabaseName);

        databases.Should().Contain("persist-message-flight", "persist_message_flight");
        databases.Should().Contain("persist-message-passenger", "persist_message_passenger");
        databases.Should().Contain("persist-message-identity", "persist_message_identity");
        databases.Should().Contain("persist-message-booking", "persist_message_booking");
        databases.Should().NotContainKey("persist-message");
    }

    [Theory]
    [InlineData("identity-service", new[] { "postgres", "identity", "persist-message-identity", "rabbitmq" })]
    [InlineData("flight-service", new[] { "postgres", "flight", "persist-message-flight", "mongo", "rabbitmq" })]
    [InlineData("passenger-service", new[] { "postgres", "passenger", "persist-message-passenger", "mongo", "rabbitmq" })]
    [InlineData("booking-service", new[] { "postgres", "eventstore", "mongo", "persist-message-booking", "rabbitmq" })]
    public void each_service_should_wait_for_its_own_dependencies(string serviceName, string[] expectedDependencies)
    {
        var service = GetResource(serviceName);

        var waitTargets = service.Annotations.OfType<WaitAnnotation>()
            .Select(w => w.Resource.Name)
            .Distinct()
            .ToList();

        waitTargets.Should().BeEquivalentTo(expectedDependencies);
    }

    [Theory]
    [InlineData("identity-service", "persist-message-identity")]
    [InlineData("flight-service", "persist-message-flight")]
    [InlineData("passenger-service", "persist-message-passenger")]
    [InlineData("booking-service", "persist-message-booking")]
    public async Task each_service_should_alias_its_outbox_database_as_persist_message(
        string serviceName,
        string outboxResourceName)
    {
        var service = GetProject(serviceName);

        var env = await service.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        env.Should().ContainKey("ConnectionStrings__persist-message")
            .WhoseValue.Should().Contain(outboxResourceName);
    }

    [Fact]
    public async Task booking_service_should_target_flight_and_passenger_grpc_endpoints()
    {
        var booking = GetProject("booking-service");

        var env = await booking.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        env.Should().Contain("Grpc__FlightAddress", "http://_grpc.flight-service");
        env.Should().Contain("Grpc__PassengerAddress", "http://_grpc.passenger-service");

        env.Keys.Should().Contain(k => k.StartsWith("services__flight-service__", StringComparison.OrdinalIgnoreCase));
        env.Keys.Should().Contain(k => k.StartsWith("services__passenger-service__", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("identity-service")]
    [InlineData("flight-service")]
    [InlineData("passenger-service")]
    [InlineData("booking-service")]
    public async Task every_service_should_use_the_identity_http_endpoint_as_jwt_authority(string serviceName)
    {
        var service = GetProject(serviceName);

        var env = await service.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        env.Should().ContainKey("Jwt__Authority")
            .WhoseValue.Should().Contain("identity-service");
    }

    [Fact]
    public async Task identity_service_issuer_uri_should_match_its_jwt_authority()
    {
        var identity = GetProject("identity-service");

        var env = await identity.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);

        env.Should().ContainKey("AuthOptions__IssuerUri");
        env["AuthOptions__IssuerUri"].Should().Be(env["Jwt__Authority"]);
    }
}
