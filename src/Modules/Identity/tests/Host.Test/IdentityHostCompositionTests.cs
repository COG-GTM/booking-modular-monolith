using System.Net;
using System.Text.Json;
using BuildingBlocks.EFCore;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using Duende.IdentityServer.Configuration;
using FluentAssertions;
using Identity;
using Identity.Data;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using TransportType = BuildingBlocks.MassTransit.TransportType;

namespace Identity.Host.Test;

public class IdentityHostCompositionTests
{
    private const string DockerEnvironment = "docker";

    private static readonly KeyValuePair<string, string?>[] QuietOverrides =
    [
        new("ObservabilityOptions:UseOTLPExporter", "false"),
        new("ObservabilityOptions:UseAspireOTLPExporter", "false"),
        new("ObservabilityOptions:UsePrometheusExporter", "false"),
    ];

    private static WebApplicationBuilder CreateBuilder(
        string environment,
        params KeyValuePair<string, string?>[] overrides
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(QuietOverrides.Concat(overrides));

        return builder;
    }

    private static JwtBearerOptions ResolveJwtBearerOptions(
        string environment,
        params KeyValuePair<string, string?>[] overrides
    )
    {
        var builder = CreateBuilder(environment, overrides);
        builder.AddIdentityHostInfrastructure();

        using var provider = builder.Services.BuildServiceProvider();

        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void production_jwt_options_use_the_identity_public_issuer_and_audience()
    {
        var options = ResolveJwtBearerOptions(Environments.Production);

        options.Authority.Should().Be("http://localhost:5101");
        options.Audience.Should().Be("booking-modular-monolith");
        options.MetadataAddress.Should().Be("http://localhost:5101/.well-known/openid-configuration");
        options.TokenValidationParameters.ValidIssuers.Should().Equal("http://localhost:5101");
        options.TokenValidationParameters.ValidAudiences.Should().Equal("booking-modular-monolith");
    }

    [Fact]
    public void docker_jwt_options_fetch_metadata_from_the_container_endpoint()
    {
        var options = ResolveJwtBearerOptions(DockerEnvironment);

        options.MetadataAddress.Should().Be("http://localhost:8080/.well-known/openid-configuration");
        options.Authority.Should().Be("http://localhost:5000");
        options.TokenValidationParameters.ValidIssuers.Should().Equal("http://localhost:5000");
    }

    [Fact]
    public void jwt_metadata_address_can_be_overridden_without_changing_the_authority()
    {
        const string metadataAddress = "http://identity-service:8080/.well-known/openid-configuration";
        var options = ResolveJwtBearerOptions(
            Environments.Production,
            new KeyValuePair<string, string?>("Jwt:MetadataAddress", metadataAddress)
        );

        options.MetadataAddress.Should().Be(metadataAddress);
        options.Authority.Should().Be("http://localhost:5101");
    }

    [Theory]
    [InlineData("Production", "localhost")]
    [InlineData(DockerEnvironment, "postgres")]
    public void identity_and_persist_message_databases_are_service_scoped(string environment, string expectedHost)
    {
        var configuration = CreateBuilder(environment).Configuration;
        var identity = new NpgsqlConnectionStringBuilder(configuration.ResolvePostgresConnectionString("Identity"));
        var persistMessage = new NpgsqlConnectionStringBuilder(
            configuration.ResolvePersistMessageConnectionString("Identity")
        );

        identity.Host.Should().Be(expectedHost);
        identity.Database.Should().Be("identity_service");
        identity.Username.Should().Be("identity_service");
        persistMessage.Host.Should().Be(expectedHost);
        persistMessage.Database.Should().Be("identity_service_persist_message");
        persistMessage.Username.Should().Be("identity_service");
        persistMessage.Database.Should().NotBe(identity.Database);
    }

    [Theory]
    [InlineData("Production", "localhost", "http://localhost:5101", "http://localhost:5101")]
    [InlineData(DockerEnvironment, "rabbitmq", "http://localhost:5000", "http://+:8080")]
    public void host_configuration_is_scoped_to_the_identity_service(
        string environment,
        string expectedRabbitMqHost,
        string expectedIssuer,
        string expectedHttpUrl
    )
    {
        var configuration = CreateBuilder(environment).Configuration;

        configuration.GetValue<TransportType>("Messaging:Transport").Should().Be(TransportType.RabbitMq);
        configuration["RabbitMqOptions:HostName"].Should().Be(expectedRabbitMqHost);
        configuration.GetValue<bool>("Modules:Identity:BackgroundProcessingEnabled").Should().BeTrue();
        configuration["ObservabilityOptions:ServiceName"].Should().Be(IdentityHostExtensions.ServiceName);
        IdentityHostExtensions.ServiceName.Should().Be("identity-service");
        configuration["AuthOptions:IssuerUri"].Should().Be(expectedIssuer);
        configuration["Kestrel:Endpoints:Http:Url"].Should().Be(expectedHttpUrl);
    }

    [Fact]
    public void docker_identity_server_issuer_matches_the_gateway_public_origin()
    {
        var builder = CreateBuilder(DockerEnvironment);
        builder.AddIdentityHostInfrastructure();
        builder.AddIdentityModules();

        using var provider = builder.Services.BuildServiceProvider();

        var identityServerOptions = provider.GetRequiredService<IOptions<IdentityServerOptions>>().Value;

        identityServerOptions.IssuerUri.Should().Be(builder.Configuration["Jwt:Authority"]);
        identityServerOptions.IssuerUri.Should().Be("http://localhost:5000");
    }

    [Fact]
    public async Task health_checks_separate_liveness_from_readiness()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.AddIdentityHostInfrastructure();

        await using var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var self = registrations.Single(registration => registration.Name == "self");
        self.Tags.Should().Contain("live").And.NotContain("ready");
        registrations
            .Where(registration => registration.Name is "postgres-identity" or "postgres-identity-persist-message")
            .Should()
            .SatisfyRespectively(
                identity =>
                {
                    identity.Tags.Should().Contain("ready").And.NotContain("live");
                    identity.FailureStatus.Should().Be(HealthStatus.Unhealthy);
                },
                persistMessage =>
                {
                    persistMessage.Tags.Should().Contain("ready").And.NotContain("live");
                    persistMessage.FailureStatus.Should().Be(HealthStatus.Unhealthy);
                }
            );
        registrations.Should().Contain(registration => registration.Name == "masstransit-bus");
        registrations
            .Where(registration => registration.Tags.Contains("live"))
            .Select(registration => registration.Name)
            .Should()
            .Equal("self");
    }

    [Fact]
    public void host_registers_only_the_identity_module_outbox_and_consumers()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.AddIdentityHostInfrastructure();
        builder.AddIdentityModules();

        var services = builder.Services;

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IdentityContext));
        services
            .Should()
            .Contain(descriptor => descriptor.ServiceType == typeof(IPersistMessageProcessor<IdentityRoot>));
        services
            .Where(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IPersistMessageProcessor<>)
            )
            .Should()
            .ContainSingle();
        services
            .Where(descriptor => descriptor.ServiceType == typeof(ConsumerModuleRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ConsumerModuleRegistration>()
            .Should()
            .OnlyContain(registration => registration.ConsumerAssembly == typeof(IdentityRoot).Assembly);
    }

    [Fact]
    public void host_dependency_graph_contains_only_identity_and_focused_building_blocks()
    {
        using var deps = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Identity.Host.deps.json"))
        );
        var libraries = deps
            .RootElement.GetProperty("libraries")
            .EnumerateObject()
            .Select(library => library.Name.Split('/')[0])
            .ToList();

        libraries
            .Should()
            .Contain(
                [
                    "Identity",
                    "Contracts.Messages",
                    "BuildingBlocks.Jwt",
                    "BuildingBlocks.MassTransit",
                    "BuildingBlocks.OpenTelemetryCollector",
                    "BuildingBlocks.ProblemDetails",
                ]
            );
        libraries
            .Should()
            .NotContain(
                ["BuildingBlocks", "BuildingBlocks.TestBase", "Flight", "Booking", "Passenger", "Api", "Gateway"]
            );
    }

    [Theory]
    [InlineData("test", HttpStatusCode.NotFound, false)]
    [InlineData("Development", HttpStatusCode.OK, true)]
    public async Task root_and_openapi_endpoints_follow_the_environment(
        string environment,
        HttpStatusCode openApiStatus,
        bool allowInMemory
    )
    {
        await using var app = await StartTestHostAsync(environment, allowInMemory);

        try
        {
            using var client = app.GetTestClient();

            (await client.GetStringAsync("/")).Should().Be(IdentityHostExtensions.ServiceName);
            (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(openApiStatus);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("test", false)]
    [InlineData("Development", true)]
    public async Task live_health_endpoint_reports_only_the_self_check(string environment, bool allowInMemory)
    {
        await using var app = await StartTestHostAsync(environment, allowInMemory);

        try
        {
            using var client = app.GetTestClient();
            using var response = await client.GetAsync("/health/live");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
            var checks = json.RootElement.GetProperty("checks");
            checks.EnumerateObject().Select(check => check.Name).Should().Equal("self");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("test", false)]
    [InlineData("Development", true)]
    public async Task ready_health_endpoint_reports_unreachable_postgres_checks(string environment, bool allowInMemory)
    {
        await using var app = await StartTestHostAsync(environment, allowInMemory);

        try
        {
            using var client = app.GetTestClient();
            using var response = await client.GetAsync("/health/ready");

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
            var checks = json.RootElement.GetProperty("checks");
            checks.GetProperty("postgres-identity").GetString().Should().Be("Unhealthy");
            checks.GetProperty("postgres-identity-persist-message").GetString().Should().Be("Unhealthy");
            checks.GetProperty("self").GetString().Should().Be("Healthy");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<WebApplication> StartTestHostAsync(string environment, bool allowInMemory)
    {
        var overrides = new List<KeyValuePair<string, string?>>
        {
            new("Messaging:Transport", nameof(TransportType.InMemory)),
            new(
                "PostgresOptions:ConnectionString:Identity",
                "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1"
            ),
            new(
                "PersistMessageOptions:ConnectionString:Identity",
                "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1"
            ),
        };
        if (allowInMemory)
            overrides.Add(new("Messaging:AllowInMemory", "true"));

        var builder = CreateBuilder(environment, overrides.ToArray());
        builder.WebHost.UseTestServer();
        builder.AddIdentityHostInfrastructure();

        var app = builder.Build();
        app.UseIdentityHostInfrastructure();
        await app.StartAsync();

        return app;
    }
}
