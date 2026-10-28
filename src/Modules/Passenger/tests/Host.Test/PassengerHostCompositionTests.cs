using BuildingBlocks.EFCore;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Grpc.AspNetCore.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Passenger;
using Passenger.Host.Extensions;
using Xunit;

namespace Host.Test;

public class PassengerHostCompositionTests
{
    private static WebApplicationBuilder CreateBuilder(
        string environment = "Production",
        params KeyValuePair<string, string?>[] overrides
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(Passenger.Host.Program).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new KeyValuePair<string, string?>[]
            {
                new("ObservabilityOptions:UseOTLPExporter", "false"),
                new("ObservabilityOptions:UseAspireOTLPExporter", "false"),
                new("ObservabilityOptions:UsePrometheusExporter", "false"),
            }.Concat(overrides)
        );
        return builder;
    }

    [Theory]
    [InlineData("Production", "localhost")]
    [InlineData("docker", "postgres")]
    public void postgres_databases_are_passenger_scoped(string environment, string expectedHost)
    {
        var configuration = CreateBuilder(environment).Configuration;
        var passenger = new NpgsqlConnectionStringBuilder(configuration.ResolvePostgresConnectionString("Passenger"));
        var persistMessage = new NpgsqlConnectionStringBuilder(
            configuration.ResolvePersistMessageConnectionString("Passenger")
        );

        passenger.Host.Should().Be(expectedHost);
        passenger.Database.Should().Be("passenger_service");
        passenger.Username.Should().Be("passenger_service");
        persistMessage.Host.Should().Be(expectedHost);
        persistMessage.Database.Should().Be("passenger_service_persist_message");
        persistMessage.Username.Should().Be("passenger_service");
    }

    [Theory]
    [InlineData("Production", "localhost", "mongodb://localhost:27017")]
    [InlineData("docker", "rabbitmq", "mongodb://mongo:27017")]
    public void host_configuration_is_passenger_scoped(string environment, string rabbitMqHost, string mongoConnection)
    {
        var configuration = CreateBuilder(environment).Configuration;

        configuration["Messaging:Transport"].Should().Be("RabbitMq");
        configuration["RabbitMqOptions:HostName"].Should().Be(rabbitMqHost);
        configuration["MongoOptions:Passenger:ConnectionString"].Should().Be(mongoConnection);
        configuration["MongoOptions:Passenger:DatabaseName"].Should().Be("passenger_service_read");
        configuration.GetValue<bool>("Modules:Passenger:BackgroundProcessingEnabled").Should().BeTrue();
        configuration["ObservabilityOptions:ServiceName"].Should().Be("passenger-service");
        configuration["ObservabilityOptions:InstrumentationName"].Should().Be("passenger-service");
    }

    [Theory]
    [InlineData("Production", "http://localhost:5103", "http://localhost:5113")]
    [InlineData("docker", "http://+:8080", "http://+:8081")]
    public void kestrel_separates_http_and_http2_grpc_endpoints(string environment, string httpUrl, string grpcUrl)
    {
        var configuration = CreateBuilder(environment).Configuration;

        configuration["Kestrel:Endpoints:Http:Url"].Should().Be(httpUrl);
        configuration["Kestrel:Endpoints:Http:Protocols"].Should().Be("Http1AndHttp2");
        configuration["Kestrel:Endpoints:Grpc:Url"].Should().Be(grpcUrl);
        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");
    }

    [Theory]
    [InlineData("Production", "https://localhost:3000", "https://localhost:3000/.well-known/openid-configuration")]
    [InlineData("docker", "http://localhost:5000", "http://identity-service:8080/.well-known/openid-configuration")]
    public async Task jwt_uses_the_configured_authority_audience_and_metadata(
        string environment,
        string authority,
        string metadata
    )
    {
        var builder = CreateBuilder(environment);
        builder.AddPassengerHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.Should().Be(authority);
        options.Audience.Should().Be("booking-modular-monolith");
        options.MetadataAddress.Should().Be(metadata);
        options.TokenValidationParameters.ValidIssuers.Should().Equal(authority);
        options.TokenValidationParameters.ValidAudiences.Should().Equal("booking-modular-monolith");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://identity-service:8080/.well-known/openid-configuration")]
    public async Task jwt_metadata_override_preserves_the_public_authority(string? metadata)
    {
        var builder = CreateBuilder("Production", new KeyValuePair<string, string?>("Jwt:MetadataAddress", metadata));
        builder.AddPassengerHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.Should().Be("https://localhost:3000");
        options
            .MetadataAddress.Should()
            .Be(string.IsNullOrEmpty(metadata) ? "https://localhost:3000/.well-known/openid-configuration" : metadata);
    }

    [Theory]
    [InlineData("Production", "RabbitMq", true)]
    [InlineData("test", "RabbitMq", true)]
    [InlineData("test", "InMemory", false)]
    [InlineData("test", null, false)]
    [InlineData("Production", null, true)]
    public async Task health_checks_separate_liveness_and_transport_specific_readiness(
        string environment,
        string? transport,
        bool rabbitMq
    )
    {
        var builder = CreateBuilder(environment, new KeyValuePair<string, string?>("Messaging:Transport", transport));
        builder.AddPassengerHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Where(check => check.Tags.Contains("live")).Select(check => check.Name).Should().Equal("self");
        var self = registrations.Single(check => check.Name == "self");
        self.Tags.Should().NotContain("ready");
        var result = await self.Factory(provider).CheckHealthAsync(new HealthCheckContext { Registration = self });
        result.Status.Should().Be(HealthStatus.Healthy);
        var dependencies = new[] { "postgres-passenger", "postgres-passenger-persist-message", "mongo-passenger" };
        foreach (var name in dependencies)
        {
            var check = registrations.Single(check => check.Name == name);
            check.Tags.Should().Contain("ready").And.NotContain("live");
            check.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        }
        registrations.Any(check => check.Name == "rabbitmq").Should().Be(rabbitMq);
        if (rabbitMq)
            registrations
                .Single(check => check.Name == "rabbitmq")
                .Tags.Should()
                .Contain("ready")
                .And.NotContain("live");
    }

    [Fact]
    public void consumers_use_only_the_passenger_assembly_and_endpoint_prefix()
    {
        var builder = CreateBuilder();
        builder.AddPassengerHostInfrastructure();
        var registrations = builder
            .Services.Where(descriptor => descriptor.ServiceType == typeof(ConsumerModuleRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ConsumerModuleRegistration>()
            .ToArray();

        registrations.Should().NotBeEmpty();
        registrations
            .Should()
            .OnlyContain(registration => registration.ConsumerAssembly == typeof(PassengerRoot).Assembly);
        registrations
            .Should()
            .OnlyContain(registration =>
                registration.EndpointName.StartsWith("passenger-service-", StringComparison.Ordinal)
            );
        registrations
            .Should()
            .Contain(registration => registration.EndpointName == "passenger-service-register-new-user-handler");
    }

    [Fact]
    public async Task grpc_health_services_include_only_liveness_checks()
    {
        var builder = CreateBuilder();
        builder.AddPassengerHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();
        var mappings = provider.GetRequiredService<IOptions<GrpcHealthChecksOptions>>().Value.Services.ToArray();

        mappings.Select(mapping => mapping.Name).Should().BeEquivalentTo("", "passenger.PassengerGrpcService");
        foreach (var mapping in mappings)
        {
            mapping.HealthCheckPredicate!(new HealthCheckMapContext("self", ["live"])).Should().BeTrue();
            mapping
                .HealthCheckPredicate!(new HealthCheckMapContext("postgres-passenger", ["ready"]))
                .Should()
                .BeFalse();
            mapping.HealthCheckPredicate!(new HealthCheckMapContext("untagged", [])).Should().BeFalse();
        }
    }

    [Fact]
    public void production_rejects_in_memory_transport()
    {
        var builder = CreateBuilder("Production", new KeyValuePair<string, string?>("Messaging:Transport", "InMemory"));

        Action configure = () => builder.AddPassengerHostInfrastructure();

        configure.Should().Throw<InvalidOperationException>().WithMessage("InMemory messaging requires*");
    }
}
