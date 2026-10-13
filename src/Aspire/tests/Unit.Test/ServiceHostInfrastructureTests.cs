using BuildingBlocks.Constants;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using FluentAssertions;
using Grpc.AspNetCore.Server;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test;

// Docker-free wiring tests for ServiceHostExtensions.AddServiceHostInfrastructure: a host built with a stub
// event mapper and no module assemblies must still get the cross-cutting services every standalone service relies on.
public class ServiceHostInfrastructureTests : IAsyncLifetime
{
    private const string ServiceName = "Stub-Service";

    private WebApplication _app = null!;
    private IServiceCollection _services = null!;

    public Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AppOptions:Name"] = ServiceName,
                ["Jwt:Authority"] = "https://identity.test",
                ["Jwt:Audience"] = "stub-api",
                ["MessageBrokerOptions:TransportType"] = "InMemory",
                ["MessageBrokerOptions:ServiceName"] = "stub-service",
                ["MessageBrokerOptions:UseInbox"] = "true",
                ["PersistMessageOptions:Interval"] = "30",
                ["PersistMessageOptions:Enabled"] = "false",
                ["PersistMessageOptions:ConnectionString"] =
                    "Server=localhost;Port=5432;Database=stub_persist_message;User Id=postgres;Password=postgres",
                ["HealthOptions:Enabled"] = "false",
                ["ObservabilityOptions:InstrumentationName"] = "stub_service",
                ["ObservabilityOptions:ServiceName"] = ServiceName,
                ["ObservabilityOptions:OTLPOptions:OTLPGrpExporterEndpoint"] = "http://localhost:4317",
                ["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpExporterEndpoint"] = "http://localhost:4319",
                ["ObservabilityOptions:ZipkinOptions:HttpExporterEndpoint"] = "http://localhost:9411/api/v2/spans",
                ["ObservabilityOptions:JaegerOptions:OTLPGrpcExporterEndpoint"] = "http://localhost:14317",
                ["ObservabilityOptions:JaegerOptions:HttpExporterEndpoint"] = "http://localhost:14268/api/traces",
            }
        );

        // Modules normally register MediatR; the outbox processor and event dispatcher take IMediator.
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ServiceHostInfrastructureTests).Assembly)
        );
        builder.Services.AddScoped<StubEventMapper>();
        builder.Services.AddScoped<OtherEventMapper>();

        builder.AddServiceHostInfrastructure<StubEventMapper>();

        _services = builder.Services;
        _app = builder.Build();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void should_wire_exactly_one_event_mapper_resolving_to_the_requested_type()
    {
        using var scope = _app.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventMapper>().Should().BeOfType<StubEventMapper>();
        scope.ServiceProvider.GetServices<IEventMapper>().Should().ContainSingle();
    }

    [Fact]
    public void should_register_the_current_user_provider_and_auth_header_handler()
    {
        using var scope = _app.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().Should().BeOfType<CurrentUserProvider>();
        scope.ServiceProvider.GetRequiredService<AuthHeaderHandler>().Should().NotBeNull();
        _services
            .Should()
            .Contain(d => d.ServiceType == typeof(IEventDispatcher) && d.ImplementationType == typeof(EventDispatcher));
    }

    [Fact]
    public void should_bind_jwt_bearer_validation_from_the_jwt_section()
    {
        var options = _app
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.Should().Be("https://identity.test");
        options.Audience.Should().Be("stub-api");
        options.TokenValidationParameters.ValidIssuers.Should().BeEquivalentTo(["https://identity.test"]);
        options.TokenValidationParameters.ValidAudiences.Should().BeEquivalentTo(["stub-api"]);
    }

    [Fact]
    public async Task should_register_the_api_scope_and_admin_policies()
    {
        var provider = _app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var apiScope = await provider.GetPolicyAsync("ApiScope");
        apiScope.Should().NotBeNull();
        apiScope!
            .Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Should()
            .ContainSingle(r => r.ClaimType == "scope" && r.AllowedValues!.Contains("stub-api"));

        (await provider.GetPolicyAsync(IdentityConstant.Role.Admin)).Should().NotBeNull();
    }

    [Fact]
    public void should_bind_message_broker_options_and_register_the_bus()
    {
        var options = _app.Services.GetRequiredService<MessageBrokerOptions>();

        options.TransportType.Should().Be(TransportType.InMemory);
        options.ServiceName.Should().Be("stub-service");
        options.UseInbox.Should().BeTrue();

        _app.Services.GetRequiredService<IBus>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_the_persist_message_outbox_and_its_background_service()
    {
        _services
            .Should()
            .Contain(d => d.ServiceType == typeof(IPersistMessageProcessor) && d.Lifetime == ServiceLifetime.Scoped);
        _services
            .Should()
            .Contain(d => d.ServiceType == typeof(IPersistMessageDbContext) && d.Lifetime == ServiceLifetime.Scoped);
        _services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PersistMessageBackgroundService)
            );
    }

    [Fact]
    public void should_add_the_grpc_exception_interceptor()
    {
        var options = _app.Services.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;

        options.Interceptors.Select(i => i.Type).Should().Contain(typeof(GrpcExceptionInterceptor));
    }

    [Fact]
    public void should_suppress_the_default_model_state_filter()
    {
        _app.Services.GetRequiredService<IOptions<ApiBehaviorOptions>>()
            .Value.SuppressModelStateInvalidFilter.Should()
            .BeTrue();
    }

    private sealed class StubEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }

    private sealed class OtherEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }
}
