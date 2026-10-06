using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Passenger.Host.Extensions;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Host.Test.Composition;

// Composes the standalone host's service graph (AddPassengerHost only, no module wiring, no
// infrastructure) and checks what the host registers for itself.
public class PassengerHostCompositionTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;

    public PassengerHostCompositionTests()
    {
        _serviceProvider = BuildHostServices();
    }

    private static ServiceProvider BuildHostServices(params KeyValuePair<string, string?>[] configurationOverrides)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ApplicationName = "Passenger.Host" }
        );
        builder.Configuration.AddJsonFile("passenger-host-appsettings.json");
        builder.Configuration.AddInMemoryCollection(configurationOverrides);

        builder.AddPassengerHost();

        return builder.Services.BuildServiceProvider();
    }

    private static JwtBearerOptions ResolveJwtBearerOptions(ServiceProvider serviceProvider) =>
        serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

    private IReadOnlyList<HealthCheckRegistration> HealthCheckRegistrations =>
        _serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToList();

    [Theory]
    [InlineData("Passenger-Postgres-Health")]
    [InlineData("Passenger-MongoDB-Health")]
    public void host_should_register_a_readiness_check_for_its_own_store(string name)
    {
        var registration = HealthCheckRegistrations.Should().ContainSingle(x => x.Name == name).Subject;

        registration.Tags.Should().Contain("ready");
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        registration.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void host_should_keep_the_liveness_self_check()
    {
        HealthCheckRegistrations.Should().Contain(x => x.Name == "self" && x.Tags.Contains("live"));
    }

    [Fact]
    public void host_should_not_register_shared_monolith_health_checks()
    {
        // HealthOptions:Enabled is false for the host, so the monolith-wide RabbitMQ/MongoDB-Health/npgsql
        // checks that read the shared options are not added
        HealthCheckRegistrations.Select(x => x.Name).Should().NotContain("MongoDB-Health");
        HealthCheckRegistrations.Select(x => x.Name).Should().NotContain("rabbitmq");
    }

    [Fact]
    public void host_should_map_user_created_to_the_passenger_consumer()
    {
        var consumerTypeMap = _serviceProvider.GetRequiredService<ConsumerTypeMap>();

        consumerTypeMap.ResolveConsumerType(typeof(UserCreated)).Should().Be<RegisterNewUserHandler>();
    }

    [Fact]
    public void host_should_only_scan_the_passenger_module_for_consumers()
    {
        var consumerTypeMap = _serviceProvider.GetRequiredService<ConsumerTypeMap>();

        // the monolith's other modules consume these; the standalone host must not
        consumerTypeMap.ResolveConsumerType(typeof(PassengerCreated)).Should().BeNull();
        consumerTypeMap.ResolveConsumerType(typeof(FlightCreated)).Should().BeNull();
    }

    [Fact]
    public void host_should_resolve_event_headers_from_the_http_context()
    {
        using var scope = _serviceProvider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<IEventHeadersProvider>()
            .Should()
            .BeOfType<HttpContextEventHeadersProvider>();
        scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().Should().BeOfType<CurrentUserProvider>();
    }

    [Fact]
    public void host_should_read_its_name_from_app_options()
    {
        var appOptions = _serviceProvider
            .GetRequiredService<IConfiguration>()
            .GetOptions<AppOptions>(nameof(AppOptions));

        appOptions.Name.Should().Be("Passenger-Service");
    }

    [Fact]
    public void host_should_validate_tokens_against_the_configured_authority()
    {
        var jwtOptions = ResolveJwtBearerOptions(_serviceProvider);

        jwtOptions.Authority.Should().Be("https://localhost:3000");
        jwtOptions.Audience.Should().Be("booking-modular-monolith");
        // without an override the metadata address is derived from the authority
        jwtOptions.MetadataAddress.Should().Be("https://localhost:3000/.well-known/openid-configuration");
    }

    [Fact]
    public async Task host_should_fetch_oidc_metadata_from_the_override_without_changing_the_authority()
    {
        await using var serviceProvider = BuildHostServices(
            new KeyValuePair<string, string?>(
                "Jwt:MetadataAddress",
                "http://booking_modular_monolith/.well-known/openid-configuration"
            )
        );

        var jwtOptions = ResolveJwtBearerOptions(serviceProvider);

        jwtOptions.MetadataAddress.Should().Be("http://booking_modular_monolith/.well-known/openid-configuration");
        jwtOptions.Authority.Should().Be("https://localhost:3000");
    }

    [Fact]
    public async Task host_should_ignore_an_empty_oidc_metadata_override()
    {
        await using var serviceProvider = BuildHostServices(
            new KeyValuePair<string, string?>("Jwt:MetadataAddress", "")
        );

        ResolveJwtBearerOptions(serviceProvider)
            .MetadataAddress.Should()
            .Be("https://localhost:3000/.well-known/openid-configuration");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();
}
