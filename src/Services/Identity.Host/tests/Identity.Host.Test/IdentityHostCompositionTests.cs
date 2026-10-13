using System.Net;
using System.Text.Json;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using Duende.IdentityServer.Configuration;
using FluentAssertions;
using Identity.Data;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using TransportType = BuildingBlocks.MassTransit.TransportType;

namespace Identity.Host.Test;

// Docker-free checks of how the standalone host is composed. The host's appsettings*.json and
// Identity.Host.deps.json are copied next to the test assembly, so the real configuration files are used.
public class IdentityHostCompositionTests
{
    private const string PublicIssuer = "http://localhost:5100";
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

    private static JwtBearerOptions ResolveJwtBearerOptions(string environment)
    {
        var builder = CreateBuilder(environment);
        builder.AddIdentityServiceInfrastructure();

        using var provider = builder.Services.BuildServiceProvider();

        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void jwt_bearer_validates_tokens_against_the_identity_service_issuer_and_audience()
    {
        var options = ResolveJwtBearerOptions(Environments.Production);

        options.Authority.Should().Be(PublicIssuer);
        options.Audience.Should().Be("booking-modular-monolith");
        options.RequireHttpsMetadata.Should().BeFalse();
        options.MetadataAddress.Should().Be($"{PublicIssuer}/.well-known/openid-configuration");
        options.TokenValidationParameters.ValidIssuers.Should().Equal(PublicIssuer);
        options.TokenValidationParameters.ValidAudiences.Should().Equal("booking-modular-monolith");
    }

    [Fact]
    public async Task api_scope_policy_requires_the_same_scope_as_the_gateway_and_sibling_hosts()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.AddIdentityServiceInfrastructure();

        using var provider = builder.Services.BuildServiceProvider();
        var policy = await provider
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(IdentityConstant.Policy.ApiScope);

        policy.Should().NotBeNull();
        policy!
            .Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Should()
            .ContainSingle(r => r.ClaimType == "scope")
            .Which.AllowedValues.Should()
            .Equal("booking-modular-monolith");
    }

    [Fact]
    public void docker_environment_fetches_jwt_metadata_in_container_but_keeps_the_public_issuer()
    {
        var options = ResolveJwtBearerOptions(DockerEnvironment);

        options.MetadataAddress.Should().Be("http://localhost/.well-known/openid-configuration");
        options.Authority.Should().Be(PublicIssuer);
        options.TokenValidationParameters.ValidIssuers.Should().Equal(PublicIssuer);
    }

    [Theory]
    [InlineData("Production", "localhost")]
    [InlineData(DockerEnvironment, "postgres")]
    public void service_owns_its_identity_and_outbox_databases(string environment, string expectedHost)
    {
        var configuration = CreateBuilder(environment).Configuration;

        var identity = new NpgsqlConnectionStringBuilder(configuration["PostgresOptions:ConnectionString:Identity"]);
        var outbox = new NpgsqlConnectionStringBuilder(
            configuration["PersistMessageOptions:ConnectionString:Identity"]
        );

        identity.Host.Should().Be(expectedHost);
        identity.Database.Should().Be("identity_service");
        outbox.Host.Should().Be(expectedHost);
        outbox.Database.Should().Be("identity_service_persist_message");
        outbox.Database.Should().NotBe(identity.Database, "the outbox has its own database");
        configuration["PostgresOptions:ConnectionString:Identity"]
            .Should()
            .NotContain("identity_modular_monolith", "the service must not share the monolith's identity database");
    }

    [Theory]
    [InlineData("Production", "localhost")]
    [InlineData(DockerEnvironment, "rabbitmq")]
    public void message_broker_is_rabbitmq_scoped_to_the_identity_service(string environment, string expectedHost)
    {
        var builder = CreateBuilder(environment);
        builder.AddIdentityServiceInfrastructure();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessageBrokerOptions>();
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();

        options.TransportType.Should().Be(TransportType.RabbitMq);
        options.ServiceName.Should().Be("identity-service");
        options.UseInbox.Should().BeTrue();
        options.RabbitMq.Host.Should().Be(expectedHost);
        formatter.Consumer<ProbeHandler>().Should().Be("identity-service-probe-handler");
    }

    [Fact]
    public void identity_server_issuer_defaults_to_auth_options_from_appsettings()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.AddIdentityServiceInfrastructure();
        builder.AddIdentityModules();

        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<IdentityServerOptions>>().Value.IssuerUri.Should().Be(PublicIssuer);
    }

    [Fact]
    public void identity_server_issuer_is_configurable_through_auth_options()
    {
        var builder = CreateBuilder(
            Environments.Production,
            new KeyValuePair<string, string?>("AuthOptions:IssuerUri", "https://auth.example.com")
        );
        builder.AddIdentityServiceInfrastructure();
        builder.AddIdentityModules();

        using var provider = builder.Services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<IdentityServerOptions>>()
            .Value.IssuerUri.Should()
            .Be("https://auth.example.com");
    }

    [Fact]
    public void host_registers_only_the_identity_module_persistence_and_outbox()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.AddIdentityServiceInfrastructure();
        builder.AddIdentityModules();

        var services = builder.Services;

        services.Should().Contain(d => d.ServiceType == typeof(IdentityContext));
        services.Should().Contain(d => d.ServiceType == typeof(IPersistMessageProcessor<IdentityRoot>));
        services.Should().Contain(d => d.ServiceType == typeof(PersistMessageDbContext<IdentityRoot>));
        services
            .Where(d =>
                d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == typeof(IPersistMessageProcessor<>)
            )
            .Should()
            .ContainSingle("the standalone host owns exactly one module outbox");
    }

    [Fact]
    public void host_dependency_graph_excludes_the_aggregate_building_blocks_test_base_and_other_modules()
    {
        using var deps = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Identity.Host.deps.json"))
        );
        var libraries = deps
            .RootElement.GetProperty("libraries")
            .EnumerateObject()
            .Select(p => p.Name.Split('/')[0])
            .ToList();

        libraries
            .Should()
            .Contain(["Identity", "Contracts.Messages", "BuildingBlocks.Jwt", "BuildingBlocks.MassTransit"]);
        libraries
            .Should()
            .NotContain(
                ["BuildingBlocks", "BuildingBlocks.TestBase", "Flight", "Booking", "Passenger", "Api", "Gateway"],
                "the host must compile only against the Identity module and focused BuildingBlocks packages"
            );
    }

    [Theory]
    [InlineData("test", HttpStatusCode.NotFound)]
    [InlineData("Development", HttpStatusCode.OK)]
    public async Task root_endpoint_probes_and_openapi_follow_the_environment(
        string environment,
        HttpStatusCode devOnlyStatus
    )
    {
        var builder = CreateBuilder(
            environment,
            new KeyValuePair<string, string?>("MessageBroker:TransportType", nameof(TransportType.InMemory)),
            new KeyValuePair<string, string?>("MessageBroker:UseInbox", "false"),
            new KeyValuePair<string, string?>("HealthOptions:DependencyChecksEnabled", "false")
        );
        builder.WebHost.UseTestServer();
        builder.AddIdentityServiceInfrastructure();

        await using var app = builder.Build();
        app.UseIdentityServiceInfrastructure();
        await app.StartAsync();

        try
        {
            using var client = app.GetTestClient();

            (await client.GetStringAsync("/")).Should().Be("Identity-Service");
            (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(devOnlyStatus);
            (await client.GetAsync("/alive"))
                .StatusCode.Should()
                .Be(HttpStatusCode.OK, "probes are mapped in every environment");
            (await client.GetAsync("/health"))
                .StatusCode.Should()
                .Be(HttpStatusCode.OK, "probes are mapped in every environment");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class ProbeHandler : IConsumer<UserCreated>
    {
        public Task Consume(ConsumeContext<UserCreated> context) => Task.CompletedTask;
    }
}
