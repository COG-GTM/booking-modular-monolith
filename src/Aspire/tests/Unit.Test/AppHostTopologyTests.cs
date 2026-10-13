using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Xunit;

namespace Unit.Test;

// Inspects the resource model registered by src/Aspire/src/AppHost/Program.cs without building/starting it
// (building would start DCP and real containers).
public class AppHostTopologyTests
{
    private static readonly string[] ServiceHosts = ["identity-api", "flight-api", "passenger-api", "booking-api"];

    [Fact]
    public async Task monolith_should_not_be_orchestrated_by_default()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();

        builder.Resources.Should().NotContain(r => r.Name == "api");
        builder.Resources.OfType<ProjectResource>().Select(r => r.Name).Should().BeEquivalentTo(ServiceHosts);
    }

    [Fact]
    public async Task monolith_should_be_orchestrated_next_to_the_services_when_opted_in()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["AppHost:IncludeMonolith=true"]
        );

        var api = builder.Resources.OfType<ProjectResource>().Should().ContainSingle(r => r.Name == "api").Subject;

        api.Annotations.OfType<EndpointAnnotation>()
            .Where(e => e.Name is "api-http" or "api-https")
            .Select(e => (e.Name, e.Port))
            .Should()
            .BeEquivalentTo([("api-http", 3001), ("api-https", 3000)]);

        builder
            .Resources.OfType<ProjectResource>()
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(["api", .. ServiceHosts]);
    }

    [Fact]
    public async Task each_service_host_should_point_at_its_own_project()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();

        var projects = builder
            .Resources.OfType<ProjectResource>()
            .ToDictionary(r => r.Name, r => Path.GetFileName(r.GetProjectMetadata().ProjectPath));

        projects.Should().Contain("identity-api", "Identity.Api.csproj");
        projects.Should().Contain("flight-api", "Flight.Api.csproj");
        projects.Should().Contain("passenger-api", "Passenger.Api.csproj");
        projects.Should().Contain("booking-api", "Booking.Api.csproj");
    }

    [Fact]
    public async Task every_service_should_own_its_write_store_and_its_outbox_store()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();

        var databases = builder
            .Resources.OfType<PostgresDatabaseResource>()
            .ToDictionary(r => r.Name, r => r.DatabaseName);

        databases.Should().Contain("identity-service-db", "identity_service");
        databases.Should().Contain("identity-service-persist-db", "identity_service_persist_message");
        databases.Should().Contain("flight-service-db", "flight_service");
        databases.Should().Contain("flight-service-persist-db", "flight_service_persist_message");
        databases.Should().Contain("passenger-service-db", "passenger_service");
        databases.Should().Contain("passenger-service-persist-db", "passenger_service_persist_message");
        databases.Should().Contain("booking-service-persist-db", "booking_service_persist_message");

        // The monolith's databases are still there for AppHost:IncludeMonolith=true.
        databases.Keys.Should().Contain(["flight", "passenger", "identity", "persist-message"]);
        builder
            .Resources.OfType<PostgresDatabaseResource>()
            .Select(r => r.Parent.Name)
            .Distinct()
            .Should()
            .Equal("postgres");
    }

    [Fact]
    public async Task service_hosts_should_receive_their_databases_under_the_module_connection_names()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();

        var identity = await EnvironmentOf(builder, "identity-api");
        identity.Should().Contain("ConnectionStrings__identity", "{identity-service-db.connectionString}");
        identity
            .Should()
            .Contain("ConnectionStrings__persist-message", "{identity-service-persist-db.connectionString}");
        identity.Should().Contain("ConnectionStrings__rabbitmq", "{rabbitmq.connectionString}");

        var flight = await EnvironmentOf(builder, "flight-api");
        flight.Should().Contain("ConnectionStrings__flight", "{flight-service-db.connectionString}");
        flight.Should().Contain("ConnectionStrings__persist-message", "{flight-service-persist-db.connectionString}");
        flight.Should().Contain("ConnectionStrings__mongo", "{mongo.connectionString}");
        flight.Should().Contain("ConnectionStrings__rabbitmq", "{rabbitmq.connectionString}");

        var passenger = await EnvironmentOf(builder, "passenger-api");
        passenger.Should().Contain("ConnectionStrings__passenger", "{passenger-service-db.connectionString}");
        passenger
            .Should()
            .Contain("ConnectionStrings__persist-message", "{passenger-service-persist-db.connectionString}");
        passenger.Should().Contain("ConnectionStrings__mongo", "{mongo.connectionString}");

        var booking = await EnvironmentOf(builder, "booking-api");
        booking.Should().Contain("ConnectionStrings__persist-message", "{booking-service-persist-db.connectionString}");
        booking.Should().Contain("ConnectionStrings__mongo", "{mongo.connectionString}");
        booking.Should().Contain("ConnectionStrings__eventstore", "{eventstore.connectionString}");
        booking.Should().Contain("ConnectionStrings__rabbitmq", "{rabbitmq.connectionString}");
        booking.Keys.Should().NotContain(k => k.StartsWith("ConnectionStrings__") && k.EndsWith("-service-db"));
    }

    [Fact]
    public async Task tokens_should_be_issued_and_validated_against_the_identity_host()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();

        const string issuer = "{identity-api.bindings.https.url}";

        var identity = await EnvironmentOf(builder, "identity-api");
        identity.Should().Contain("AuthOptions__IssuerUri", issuer);
        identity.Should().Contain("Jwt__Authority", issuer);

        foreach (var host in ServiceHosts.Where(h => h != "identity-api"))
        {
            var env = await EnvironmentOf(builder, host);
            env.Should()
                .Contain("Jwt__Authority", issuer, because: $"{host} must validate tokens against the Identity host");
            WaitsFor(builder, host).Should().Contain("identity-api");
        }
    }

    [Fact]
    public async Task booking_should_call_flight_and_passenger_grpc_on_their_https_endpoints()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();

        var booking = await EnvironmentOf(builder, "booking-api");

        booking.Should().Contain("Grpc__FlightAddress", "{flight-api.bindings.https.url}");
        booking.Should().Contain("Grpc__PassengerAddress", "{passenger-api.bindings.https.url}");
        booking.Keys.Should().Contain(k => k.StartsWith("services__flight-api__"));
        booking.Keys.Should().Contain(k => k.StartsWith("services__passenger-api__"));

        WaitsFor(builder, "booking-api").Should().Contain(["identity-api", "flight-api", "passenger-api"]);
    }

    private static async Task<Dictionary<string, string>> EnvironmentOf(
        IDistributedApplicationTestingBuilder builder,
        string name
    )
    {
        var resource = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == name);

        return await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
    }

    private static IEnumerable<string> WaitsFor(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.Single(r => r.Name == name).Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name);
}
