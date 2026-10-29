using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Projects;
using Xunit;

namespace AppHost.Test;

public class AppHostServicesWiringTests
{
    [Fact]
    public async Task Topology_is_case_insensitive()
    {
        await using var monolithBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--AppHost:Topology=monolith"]
        );
        var monolithProjects = monolithBuilder
            .Resources.OfType<ProjectResource>()
            .Select(resource => resource.Name)
            .ToHashSet();

        Assert.Contains("api", monolithProjects);
        Assert.DoesNotContain("identity-service", monolithProjects);

        await using var servicesBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--AppHost:Topology=services"]
        );
        var servicesProjects = servicesBuilder
            .Resources.OfType<ProjectResource>()
            .Select(resource => resource.Name)
            .ToHashSet();

        Assert.Contains("gateway", servicesProjects);
        Assert.Contains("identity-service", servicesProjects);
        Assert.Contains("flight-service", servicesProjects);
        Assert.Contains("passenger-service", servicesProjects);
        Assert.Contains("booking-service", servicesProjects);
        Assert.DoesNotContain("api", servicesProjects);
    }

    [Fact]
    public async Task Unsupported_topology_throws()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                ["--AppHost:Topology=Bogus"]
            );
        });

        var exceptionMessage = exception.ToString();
        Assert.Contains("Unsupported AppHost topology 'Bogus'", exceptionMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_hosts_expose_only_pinned_endpoints()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);

        var gatewayHttp = projects["gateway"]
            .Annotations.OfType<EndpointAnnotation>()
            .Single(annotation => annotation.Name == "http");
        Assert.Equal(5000, gatewayHttp.Port);

        var expected = new Dictionary<string, Dictionary<string, int>>
        {
            ["identity-service"] = new() { ["http"] = 5101 },
            ["flight-service"] = new() { ["http"] = 5102, ["grpc"] = 5112 },
            ["passenger-service"] = new() { ["http"] = 5103, ["grpc"] = 5113 },
            ["booking-service"] = new() { ["http"] = 5104 },
        };

        foreach (var (projectName, expectedEndpoints) in expected)
        {
            var endpoints = projects[projectName].Annotations.OfType<EndpointAnnotation>().ToList();
            Assert.Equal(
                expectedEndpoints.Keys.OrderBy(name => name).ToArray(),
                endpoints.Select(endpoint => endpoint.Name).OrderBy(name => name).ToArray()
            );

            Assert.All(
                endpoints,
                endpoint =>
                {
                    Assert.Equal(expectedEndpoints[endpoint.Name], endpoint.Port);
                    Assert.False(endpoint.IsProxied);
                    Assert.Equal(endpoint.Port, endpoint.TargetPort);

                    if (endpoint.Name == "grpc")
                    {
                        Assert.Equal("http", endpoint.UriScheme);
                        Assert.False(endpoint.IsExternal);
                    }
                }
            );
        }
    }

    [Theory]
    [InlineData("identity", "identity-service", "Identity")]
    [InlineData("flight", "flight-service", "Flight")]
    [InlineData("passenger", "passenger-service", "Passenger")]
    public async Task Services_get_module_specific_option_keys(string module, string projectName, string moduleName)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var project = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == projectName);
        var environment = await EnvironmentAsync(project);

        AssertConnectionValue(
            environment[$"PostgresOptions__ConnectionString__{moduleName}"],
            $"{module}_service",
            $"{module}_service"
        );
        AssertConnectionValue(
            environment[$"PersistMessageOptions__ConnectionString__{moduleName}"],
            $"{module}_service_persist_message",
            $"{module}_service"
        );
    }

    [Fact]
    public async Task Booking_gets_only_its_persist_message_option_key()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var booking = builder
            .Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "booking-service");
        var environment = await EnvironmentAsync(booking);

        AssertConnectionValue(
            environment["PersistMessageOptions__ConnectionString__Booking"],
            "booking_service_persist_message",
            "booking_service"
        );
        Assert.DoesNotContain("PostgresOptions__ConnectionString__Booking", environment.Keys);
    }

    [Fact]
    public async Task Infrastructure_is_wired_only_where_used()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);
        var identity = await EnvironmentAsync(projects["identity-service"]);
        var flight = await EnvironmentAsync(projects["flight-service"]);
        var passenger = await EnvironmentAsync(projects["passenger-service"]);
        var booking = await EnvironmentAsync(projects["booking-service"]);

        Assert.Contains("MongoOptions__ConnectionString", flight.Keys);
        Assert.Contains("MongoOptions__Flight__ConnectionString", flight.Keys);
        Assert.Contains("MongoOptions__Passenger__ConnectionString", passenger.Keys);
        Assert.Contains("MongoOptions__ConnectionString", booking.Keys);
        Assert.DoesNotContain(identity.Keys, key => key.StartsWith("MongoOptions__", StringComparison.Ordinal));
        Assert.DoesNotContain("ConnectionStrings__mongo", identity.Keys);

        Assert.Contains("ConnectionStrings__eventstore", booking.Keys);
        Assert.DoesNotContain("ConnectionStrings__eventstore", identity.Keys);
        Assert.DoesNotContain("ConnectionStrings__eventstore", flight.Keys);
        Assert.DoesNotContain("ConnectionStrings__eventstore", passenger.Keys);

        foreach (
            var (projectName, moduleName) in new[]
            {
                ("identity-service", "Identity"),
                ("flight-service", "Flight"),
                ("passenger-service", "Passenger"),
                ("booking-service", "Booking"),
            }
        )
        {
            var environment = await EnvironmentAsync(projects[projectName]);
            Assert.Contains("ConnectionStrings__rabbitmq", environment.Keys);
            Assert.Equal("true", environment[$"Modules__{moduleName}__BackgroundProcessingEnabled"]);
        }
    }

    [Fact]
    public async Task Startup_ordering_waits_for_required_dependencies()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);

        AssertWaitTargets(
            projects["gateway"],
            "identity-service",
            "flight-service",
            "passenger-service",
            "booking-service"
        );
        AssertWaitTargets(projects["booking-service"], "flight-service", "passenger-service", "eventstore", "postgres");
        AssertWaitTargets(projects["identity-service"], "postgres", "rabbitmq");
        AssertWaitTargets(projects["passenger-service"], "postgres", "rabbitmq");
        AssertWaitTargets(projects["flight-service"], "postgres");
    }

    [Fact]
    public async Task Jwt_metadata_comes_from_identity_endpoint()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);

        foreach (
            var name in new[]
            {
                "flight-service",
                "passenger-service",
                "booking-service",
                "gateway",
                "identity-service",
            }
        )
        {
            var environment = await EnvironmentAsync(projects[name]);
            var metadataAddress = environment["Jwt__MetadataAddress"];
            Assert.Contains("identity-service", metadataAddress, StringComparison.Ordinal);
            Assert.EndsWith("/.well-known/openid-configuration", metadataAddress, StringComparison.Ordinal);
            Assert.Equal("http://localhost:5000", environment["Jwt__Authority"]);
        }

        var gateway = await EnvironmentAsync(projects["gateway"]);
        Assert.Contains(
            "identity-service",
            gateway["ReverseProxy__Clusters__monolith__Destinations__primary__Address"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Monolith_topology_wires_module_options_and_health_checks()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            ["--AppHost:Topology=Monolith"]
        );
        var projects = builder.Resources.OfType<ProjectResource>().ToDictionary(resource => resource.Name);
        var api = await EnvironmentAsync(projects["api"]);
        var gateway = await EnvironmentAsync(projects["gateway"]);

        AssertOtelPublishEnvironment(projects["api"], api);
        AssertOtelPublishEnvironment(projects["gateway"], gateway);
        var gatewayHealthChecks = projects["gateway"].Annotations.OfType<HealthCheckAnnotation>();
        Assert.NotEmpty(gatewayHealthChecks);

        foreach (var environment in new[] { api, gateway })
        {
            var metadataAddress = environment["Jwt__MetadataAddress"];
            Assert.Contains("api", metadataAddress, StringComparison.Ordinal);
            Assert.EndsWith("/.well-known/openid-configuration", metadataAddress, StringComparison.Ordinal);
        }

        Assert.Contains("api-https", api["Grpc__FlightAddress"], StringComparison.Ordinal);
        Assert.Contains("api-https", api["Grpc__PassengerAddress"], StringComparison.Ordinal);

        foreach (var module in new[] { "Flight", "Identity", "Passenger" })
        {
            Assert.Contains($"PostgresOptions__ConnectionString__{module}", api.Keys);
        }

        Assert.DoesNotContain("PostgresOptions__ConnectionString__Booking", api.Keys);

        foreach (var module in new[] { "Flight", "Identity", "Passenger", "Booking" })
        {
            Assert.Contains($"PersistMessageOptions__ConnectionString__{module}", api.Keys);
        }
    }

    [Fact]
    public async Task Observability_extension_uses_ab495_environment_for_every_services_project()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var projects = builder.Resources.OfType<ProjectResource>();

        foreach (var project in projects)
        {
            var environment = await EnvironmentAsync(project);
            AssertOtelPublishEnvironment(project, environment);
        }
    }

    [Fact]
    public async Task Collector_otlp_callback_overrides_aspire_endpoint_and_removes_dashboard_headers()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var booking = builder
            .Resources.OfType<ProjectResource>()
            .Single(resource => resource.Name == "booking-service");

        var seededEnvironment = new Dictionary<string, object>
        {
            ["OTEL_SERVICE_NAME"] = "booking-service",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4999",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "x-otlp-api-key=dashboard-key",
        };
        await RunEnvironmentCallbacksAsync(booking, seededEnvironment);
        Assert.Equal("booking-service", seededEnvironment["OTEL_SERVICE_NAME"]?.ToString());
        Assert.Equal("grpc", seededEnvironment["OTEL_EXPORTER_OTLP_PROTOCOL"]?.ToString());
        Assert.DoesNotContain("OTEL_EXPORTER_OTLP_HEADERS", seededEnvironment.Keys);
        Assert.IsType<EndpointReference>(seededEnvironment["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }

    [Fact]
    public async Task Infrastructure_images_and_endpoints_are_pinned()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>([]);
        var resources = builder.Resources.ToDictionary(resource => resource.Name);

        var grafanaHttp = resources["grafana"]
            .Annotations.OfType<EndpointAnnotation>()
            .Single(annotation => annotation.Name == "http");
        Assert.Equal(3030, grafanaHttp.Port);
        Assert.Equal(3000, grafanaHttp.TargetPort);

        var tempoImage = resources["tempo"].Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("2.7.2", tempoImage.Tag);
        foreach (var nameAndPort in new[] { ("otlp-grpc", 4317), ("otlp-http", 4318) })
        {
            var endpoint = resources["tempo"]
                .Annotations.OfType<EndpointAnnotation>()
                .Single(annotation => annotation.Name == nameAndPort.Item1);
            Assert.Null(endpoint.Port);
            Assert.Equal(nameAndPort.Item2, endpoint.TargetPort);
        }

        var elasticsearchImage = resources["elasticsearch"].Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("docker.elastic.co", elasticsearchImage.Registry);
        Assert.Equal("elasticsearch/elasticsearch", elasticsearchImage.Image);
        Assert.Equal("8.17.0", elasticsearchImage.Tag);
    }

    private static async Task<Dictionary<string, string>> EnvironmentAsync(
        ProjectResource resource,
        DistributedApplicationOperation applicationOperation = DistributedApplicationOperation.Publish
    )
    {
        var variables = await resource.GetEnvironmentVariableValuesAsync(applicationOperation);
        return variables.ToDictionary(variable => variable.Key, variable => variable.Value?.ToString() ?? "");
    }

    private static void AssertOtelPublishEnvironment(
        ProjectResource project,
        Dictionary<string, string> environment
    )
    {
        Assert.Single(project.Annotations.OfType<OtlpExporterAnnotation>());
        Assert.Contains("otel-collector", environment["OTEL_EXPORTER_OTLP_ENDPOINT"], StringComparison.Ordinal);
        Assert.Equal("grpc", environment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.DoesNotContain("OTEL_EXPORTER_OTLP_HEADERS", environment.Keys);
        Assert.DoesNotContain("ObservabilityOptions__ServiceName", environment.Keys);
        Assert.DoesNotContain("ObservabilityOptions__UseOTLPExporter", environment.Keys);
        Assert.DoesNotContain("ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint", environment.Keys);
        Assert.DoesNotContain("ObservabilityOptions__UseAspireOTLPExporter", environment.Keys);
        Assert.DoesNotContain(
            "ObservabilityOptions__AspireDashboardOTLPOptions__OTLPGrpcExporterEndpoint",
            environment.Keys
        );
    }

    private static void AssertConnectionValue(string value, string database, string username)
    {
        Assert.Contains($"Database={database}", value, StringComparison.Ordinal);
        Assert.Contains($"Username={username}", value, StringComparison.Ordinal);
    }

    private static void AssertWaitTargets(ProjectResource resource, params string[] expected)
    {
        var actual = resource
            .Annotations.OfType<WaitAnnotation>()
            .Select(annotation => annotation.Resource.Name)
            .OrderBy(name => name);
        var expectedTargets = expected.OrderBy(name => name).ToArray();
        var actualTargets = actual.ToArray();
        Assert.Equal(expectedTargets, actualTargets);
    }

    private static async Task RunEnvironmentCallbacksAsync(
        ProjectResource resource,
        Dictionary<string, object> environmentVariables
    )
    {
        var executionContext = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish);
        var context = new EnvironmentCallbackContext(executionContext, resource, environmentVariables);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList())
        {
            await annotation.Callback(context);
        }
    }
}
