using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Projects;
using Xunit;

namespace AppHost.Test;

public class AppHostTopologyTests
{
    [Fact]
    public async Task Services_are_the_only_project_topology()
    {
        await using var servicesBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        await using var servicesApp = servicesBuilder.Build();
        var servicesModel = servicesApp.Services.GetRequiredService<DistributedApplicationModel>();
        var serviceProjects = servicesModel
            .Resources.OfType<ProjectResource>()
            .Select(resource => resource.Name)
            .ToHashSet();

        Assert.Contains("gateway", serviceProjects);
        Assert.Contains("identity-service", serviceProjects);
        Assert.Contains("flight-service", serviceProjects);
        Assert.Contains("passenger-service", serviceProjects);
        Assert.Contains("booking-service", serviceProjects);
        Assert.DoesNotContain("api", serviceProjects);

        Assert.Equal(5, serviceProjects.Count);
    }

    [Fact]
    public async Task Services_receive_only_their_own_postgres_databases()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        await using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var projects = model.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);
        var connectionStrings = model
            .Resources.OfType<ConnectionStringResource>()
            .ToDictionary(resource => resource.Name);
        var identity = await EnvironmentAsync(projects["identity-service"]);
        var flight = await EnvironmentAsync(projects["flight-service"]);
        var passenger = await EnvironmentAsync(projects["passenger-service"]);
        var booking = await EnvironmentAsync(projects["booking-service"]);

        Assert.Contains("ConnectionStrings__identity", identity.Keys);
        Assert.Contains("ConnectionStrings__identity-persist-message", identity.Keys);
        Assert.Contains("ConnectionStrings__flight", flight.Keys);
        Assert.Contains("ConnectionStrings__flight-persist-message", flight.Keys);
        Assert.Contains("ConnectionStrings__passenger", passenger.Keys);
        Assert.Contains("ConnectionStrings__passenger-persist-message", passenger.Keys);
        Assert.Contains("ConnectionStrings__booking-persist-message", booking.Keys);

        AssertPostgresConnection(connectionStrings["identity"], "identity_service");
        AssertPostgresConnection(connectionStrings["identity-persist-message"], "identity_service_persist_message");
        AssertPostgresConnection(connectionStrings["flight"], "flight_service");
        AssertPostgresConnection(connectionStrings["flight-persist-message"], "flight_service_persist_message");
        AssertPostgresConnection(connectionStrings["passenger"], "passenger_service");
        AssertPostgresConnection(connectionStrings["passenger-persist-message"], "passenger_service_persist_message");
        AssertPostgresConnection(connectionStrings["booking-persist-message"], "booking_service_persist_message");

        Assert.DoesNotContain("ConnectionStrings__flight", identity.Keys);
        Assert.DoesNotContain("ConnectionStrings__identity", flight.Keys);
        Assert.DoesNotContain("ConnectionStrings__booking-persist-message", flight.Keys);
        Assert.DoesNotContain("ConnectionStrings__identity", passenger.Keys);
        Assert.DoesNotContain("ConnectionStrings__flight", booking.Keys);
        Assert.DoesNotContain("ConnectionStrings__identity", booking.Keys);
        Assert.DoesNotContain("ConnectionStrings__passenger", booking.Keys);
    }

    [Fact]
    public async Task Booking_uses_service_discovery_for_grpc_dependencies()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        await using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var bookingProject = model
            .Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "booking-service");
        var booking = await EnvironmentAsync(bookingProject);

        Assert.Equal("http://_grpc.flight-service", booking["Grpc__FlightAddress"]);
        Assert.Equal("http://_grpc.passenger-service", booking["Grpc__PassengerAddress"]);
        Assert.Contains("services__flight-service__grpc__0", booking.Keys);
        Assert.Contains("services__passenger-service__grpc__0", booking.Keys);
    }

    [Fact]
    public async Task Gateway_routes_to_service_hosts_and_identity_uses_its_public_issuer()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        await using var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var projects = model.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);
        var gateway = await EnvironmentAsync(projects["gateway"]);
        var identity = await EnvironmentAsync(projects["identity-service"]);

        Assert.DoesNotContain(
            gateway.Keys,
            key =>
                key.StartsWith("ReverseProxy__Clusters__monolith__", StringComparison.Ordinal)
                || key.StartsWith("ReverseProxy__Routes__fallback__", StringComparison.Ordinal)
        );
        AssertClusterDestination(gateway, "identity", "identity-service");
        AssertClusterDestination(gateway, "flight", "flight-service");
        AssertClusterDestination(gateway, "passenger", "passenger-service");
        AssertClusterDestination(gateway, "booking", "booking-service");
        Assert.Equal("http://localhost:5000", identity["Jwt__Authority"]);
        Assert.Equal("http://localhost:5000", identity["AuthOptions__IssuerUri"]);
    }

    [Fact]
    public async Task Every_services_topology_project_has_health_and_observability_annotations()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        await using var app = builder.Build();
        var projects = app
            .Services.GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<ProjectResource>()
            .ToDictionary(resource => resource.Name);

        foreach (
            var name in new[]
            {
                "gateway",
                "identity-service",
                "flight-service",
                "passenger-service",
                "booking-service",
            }
        )
        {
            var project = projects[name];
            var healthChecks = project.Annotations.OfType<HealthCheckAnnotation>().ToList();
            Assert.NotEmpty(healthChecks);
            Assert.Single(project.Annotations.OfType<OtlpExporterAnnotation>());
            Assert.Equal(name, project.Name);
            var environment = await EnvironmentAsync(project);
            var runtimeEnvironment = await EnvironmentAsync(project, DistributedApplicationOperation.Run);
            Assert.Contains(
                ".Annotations \"otel-service-name\"",
                runtimeEnvironment["OTEL_SERVICE_NAME"],
                StringComparison.Ordinal
            );
            Assert.Contains("otel-collector", environment["OTEL_EXPORTER_OTLP_ENDPOINT"], StringComparison.Ordinal);
            Assert.StartsWith(
                "http://localhost:",
                runtimeEnvironment["OTEL_EXPORTER_OTLP_ENDPOINT"],
                StringComparison.Ordinal
            );
            Assert.Equal("grpc", runtimeEnvironment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
            Assert.DoesNotContain("OTEL_EXPORTER_OTLP_HEADERS", runtimeEnvironment.Keys);
        }
    }

    private static async Task<Dictionary<string, string>> EnvironmentAsync(
        ProjectResource resource,
        DistributedApplicationOperation applicationOperation = DistributedApplicationOperation.Publish
    )
    {
        var variables = await resource.GetEnvironmentVariableValuesAsync(applicationOperation);
        return variables.ToDictionary(variable => variable.Key, variable => variable.Value?.ToString() ?? "");
    }

    private static void AssertPostgresConnection(ConnectionStringResource resource, string database)
    {
        var role = database.Split('_')[0] + "_service";
        var format = resource.ConnectionStringExpression.Format;
        Assert.Contains($"Database={database}", format, StringComparison.Ordinal);
        Assert.Contains($"Username={role}", format, StringComparison.Ordinal);
        Assert.Contains($"Password={role}", format, StringComparison.Ordinal);
    }

    private static void AssertClusterDestination(
        IReadOnlyDictionary<string, string> environment,
        string cluster,
        string project
    )
    {
        var address = environment[$"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address"];
        Assert.Contains(project, address, StringComparison.Ordinal);
        Assert.DoesNotContain("api", address, StringComparison.Ordinal);
    }
}
