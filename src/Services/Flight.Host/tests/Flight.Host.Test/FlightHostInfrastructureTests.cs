using System.Net;
using Asp.Versioning;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using EasyCaching.Core;
using Flight.Host.Extensions;
using FluentAssertions;
using Grpc.AspNetCore.HealthChecks;
using Grpc.AspNetCore.Server;
using Grpc.Health.V1;
using Grpc.Net.Client;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flight.Host.Test;

// Docker-free checks of AddFlightHostInfrastructure/UseFlightHostInfrastructure: the shared,
// non-module half of the host must be self-contained (no Flight module registered here).
public class FlightHostInfrastructureTests
{
    [Fact]
    public void grpc_health_should_map_the_overall_and_flight_grpc_service_names_only()
    {
        var builder = CreateBuilder();
        using var provider = builder.Services.BuildServiceProvider();

        var names = provider
            .GetRequiredService<IOptions<GrpcHealthChecksOptions>>()
            .Value.Services.Select(mapping => mapping.Name)
            .ToList();

        names.Should().Contain(string.Empty);
        names.Should().Contain(FlightGrpcService.Descriptor.FullName);
        names.Should().NotContain(PassengerGrpcService.Descriptor.FullName);
    }

    [Fact]
    public void grpc_services_should_use_the_shared_exception_interceptor()
    {
        var builder = CreateBuilder();
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;

        options.Interceptors.Should().ContainSingle(i => i.Type == typeof(GrpcExceptionInterceptor));
    }

    [Fact]
    public void mvc_should_suppress_the_automatic_model_state_filter()
    {
        var builder = CreateBuilder();
        using var provider = builder.Services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<ApiBehaviorOptions>>()
            .Value.SuppressModelStateInvalidFilter.Should()
            .BeTrue();
    }

    [Fact]
    public async Task rest_should_authenticate_with_jwt_bearer_bound_to_the_jwt_section()
    {
        var builder = CreateBuilder();
        using var provider = builder.Services.BuildServiceProvider();

        var defaultScheme = await provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        defaultScheme!.Name.Should().Be(JwtBearerDefaults.AuthenticationScheme);
        jwt.Authority.Should().Be(builder.Configuration["Jwt:Authority"]);
        jwt.Audience.Should().Be(builder.Configuration["Jwt:Audience"]);
        jwt.RequireHttpsMetadata.Should().BeFalse();
    }

    [Fact]
    public void current_user_provider_should_be_scoped_and_auth_header_handler_transient()
    {
        var builder = CreateBuilder();

        builder
            .Services.Should()
            .ContainSingle(d => d.ServiceType == typeof(ICurrentUserProvider))
            .Which.Should()
            .Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(CurrentUserProvider) && d.Lifetime == ServiceLifetime.Scoped
            );
        builder
            .Services.Should()
            .ContainSingle(d => d.ServiceType == typeof(AuthHeaderHandler))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void message_broker_should_prefix_endpoint_names_with_the_flight_service_name()
    {
        var builder = CreateBuilder();
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<MessageBrokerOptions>();
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();

        options.ServiceName.Should().Be("flight");
        options.UseInbox.Should().BeTrue();
        formatter.Consumer<ProbeHandler>().Should().Be("flight-probe-handler");
    }

    [Fact]
    public void infrastructure_should_register_named_cache_versioning_problem_details_and_event_dispatcher()
    {
        var builder = CreateBuilder();
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IEasyCachingProviderFactory>().GetCachingProvider("mem").Should().NotBeNull();
        provider
            .GetRequiredService<IOptions<ApiVersioningOptions>>()
            .Value.DefaultApiVersion.ToString()
            .Should()
            .Be("1.0");
        provider.GetRequiredService<IProblemDetailsService>().Should().NotBeNull();
        provider.GetRequiredService<IEventDispatcher>().Should().NotBeNull();
    }

    [Fact]
    public async Task host_infrastructure_should_boot_without_the_flight_module_and_serve_root_and_grpc_health()
    {
        var builder = CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseFlightHostInfrastructure();
        await app.StartAsync();

        using var client = app.GetTestClient();
        (await client.GetStringAsync("/")).Should().Be("Flight-Service");

        var openApi = await client.GetAsync("/openapi/v1.json");
        openApi.StatusCode.Should().Be(HttpStatusCode.NotFound, "OpenAPI is only mapped in Development");

        using var channel = GrpcChannel.ForAddress(
            client.BaseAddress!,
            new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() }
        );
        var health = new Health.HealthClient(channel);

        var overall = await health.CheckAsync(new HealthCheckRequest { Service = string.Empty });
        var flight = await health.CheckAsync(
            new HealthCheckRequest { Service = FlightGrpcService.Descriptor.FullName }
        );

        overall.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
        flight.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);

        await app.StopAsync();
    }

    [Fact]
    public async Task development_environment_should_serve_openapi_document()
    {
        var builder = CreateBuilder("Development");
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseFlightHostInfrastructure();
        await app.StartAsync();

        using var client = app.GetTestClient();
        var openApi = await client.GetAsync("/openapi/v1.json");
        openApi.StatusCode.Should().Be(HttpStatusCode.OK);

        await app.StopAsync();
    }

    // Loads the host's real appsettings.json from the test output, but keeps the bus and telemetry
    // off the network.
    private static WebApplicationBuilder CreateBuilder(string environmentName = "test")
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environmentName,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );

        builder.Configuration["MessageBroker:TransportType"] = nameof(
            BuildingBlocks.MassTransit.TransportType.InMemory
        );
        builder.Configuration["ObservabilityOptions:UseOTLPExporter"] = "false";
        builder.Configuration["ObservabilityOptions:UseAspireOTLPExporter"] = "false";
        builder.Configuration["HealthOptions:DependencyChecksEnabled"] = "false";

        builder.AddFlightHostInfrastructure();

        return builder;
    }

    public sealed record ProbeMessage;

    public sealed class ProbeHandler : IConsumer<ProbeMessage>
    {
        public Task Consume(ConsumeContext<ProbeMessage> context) => Task.CompletedTask;
    }
}
