using Booking.Configuration;
using Booking.Extensions.Infrastructure;
using Booking.GrpcClient;
using Contracts.Grpc.Flight;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Xunit;
using GetByIdRequest = Contracts.Grpc.Flight.GetByIdRequest;

namespace Unit.Test.GrpcClient;

// Inspects what AddGrpcClients registers (channel service config, interceptors, handler chain,
// health-check metadata) without needing a server for most cases.
public class GrpcClientConfigurationTests
{
    private const string FlightHealthClientName = GrpcClientExtensions.FlightClientName + "-health";
    private const string PassengerHealthClientName = GrpcClientExtensions.PassengerClientName + "-health";

    [Fact]
    public void options_defaults_match_the_documented_values()
    {
        var options = new GrpcOptions();

        options.Deadline.Should().Be(TimeSpan.FromSeconds(10));
        options.MaxAttempts.Should().Be(3);
        options.InitialBackoff.Should().Be(TimeSpan.FromMilliseconds(200));
        options.MaxBackoff.Should().Be(TimeSpan.FromSeconds(2));
        options.AllowUntrustedServerCertificate.Should().BeFalse();
        options.HealthChecksEnabled.Should().BeTrue();
        options.FlightAddress.Should().BeNull();
        options.PassengerAddress.Should().BeNull();
    }

    [Fact]
    public void retry_policy_mirrors_the_configured_options()
    {
        using var provider = BuildProvider(
            new()
            {
                ["Grpc:MaxAttempts"] = "5",
                ["Grpc:InitialBackoff"] = "00:00:01",
                ["Grpc:MaxBackoff"] = "00:00:04",
            }
        );

        var serviceConfig = GetServiceConfig(provider, GrpcClientExtensions.FlightClientName);

        var methodConfig = serviceConfig.MethodConfigs.Should().ContainSingle().Which;
        methodConfig.RetryPolicy.Should().NotBeNull();
        methodConfig.RetryPolicy!.MaxAttempts.Should().Be(5);
        methodConfig.RetryPolicy.InitialBackoff.Should().Be(TimeSpan.FromSeconds(1));
        methodConfig.RetryPolicy.MaxBackoff.Should().Be(TimeSpan.FromSeconds(4));
        methodConfig.RetryPolicy.BackoffMultiplier.Should().Be(2);
        methodConfig.RetryPolicy.RetryableStatusCodes.Should().BeEquivalentTo([StatusCode.Unavailable]);
    }

    [Theory]
    [InlineData(GrpcClientExtensions.FlightClientName, "flight.FlightGrpcService", "GetById", "GetAvailableSeats")]
    [InlineData(GrpcClientExtensions.PassengerClientName, "passenger.PassengerGrpcService", "GetById")]
    [InlineData(FlightHealthClientName, "grpc.health.v1.Health", "Check")]
    [InlineData(PassengerHealthClientName, "grpc.health.v1.Health", "Check")]
    public void only_read_only_rpcs_are_retryable(string clientName, string service, params string[] methods)
    {
        using var provider = BuildProvider();

        var names = GetServiceConfig(provider, clientName).MethodConfigs.Single().Names;

        names.Should().NotContain(MethodName.Default, "a catch-all policy would also retry non-idempotent calls");
        names.Should().AllSatisfy(name => name.Service.Should().Be(service));
        names.Select(name => name.Method).Should().BeEquivalentTo(methods);
    }

    [Fact]
    public void retry_policy_uses_defaults_when_only_addresses_are_configured()
    {
        using var provider = BuildProvider();

        var retryPolicy = GetServiceConfig(provider, GrpcClientExtensions.FlightClientName)
            .MethodConfigs.Single()
            .RetryPolicy;

        retryPolicy.Should().NotBeNull();
        retryPolicy!.MaxAttempts.Should().Be(3);
        retryPolicy.InitialBackoff.Should().Be(TimeSpan.FromMilliseconds(200));
        retryPolicy.MaxBackoff.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void single_attempt_disables_the_retry_policy()
    {
        using var provider = BuildProvider(new() { ["Grpc:MaxAttempts"] = "1" });

        GetServiceConfig(provider, GrpcClientExtensions.FlightClientName).MethodConfigs.Should().BeEmpty();
    }

    [Fact]
    public async Task single_attempt_surfaces_the_first_unavailable_error()
    {
        await using var server = await GrpcTestServer.StartAsync();
        server.State.FailuresBeforeSuccess = 1;
        await using var provider = BuildProvider(new() { ["Grpc:MaxAttempts"] = "1" }, server.Address);

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = "42" });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        server.State.GetByIdCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(GrpcClientExtensions.FlightClientName)]
    [InlineData(GrpcClientExtensions.PassengerClientName)]
    [InlineData(FlightHealthClientName)]
    [InlineData(PassengerHealthClientName)]
    public void every_client_gets_address_retry_config_and_deadline_interceptor(string clientName)
    {
        using var provider = BuildProvider(new() { ["Grpc:Deadline"] = "00:00:07" });

        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>().Get(clientName);

        options.Address.Should().Be(new Uri("http://127.0.0.1:1"));
        GetServiceConfig(provider, clientName).MethodConfigs.Single().RetryPolicy.Should().NotBeNull();

        var interceptors = options.InterceptorRegistrations.Select(r => r.Creator(provider)).ToList();
        interceptors.Should().ContainSingle(i => i is GrpcDeadlineInterceptor);
    }

    [Fact]
    public void health_check_registrations_carry_readiness_metadata_and_deadline_timeout()
    {
        using var provider = BuildProvider(new() { ["Grpc:Deadline"] = "00:00:03" });

        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Select(r => r.Name).Should().BeEquivalentTo("flight-grpc", "passenger-grpc");
        registrations
            .Should()
            .AllSatisfy(registration =>
            {
                registration
                    .Tags.Should()
                    .BeEquivalentTo(GrpcClientExtensions.ReadinessTag, GrpcClientExtensions.GrpcDependencyTag);
                registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
                registration.Timeout.Should().Be(TimeSpan.FromSeconds(3));
                registration.Factory(provider).Should().BeOfType<GrpcDependencyHealthCheck>();
            });
    }

    [Fact]
    public void health_checks_can_be_disabled()
    {
        using var provider = BuildProvider(new() { ["Grpc:HealthChecksEnabled"] = "false" });

        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Should().BeEmpty();
        provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get(FlightHealthClientName)
            .Address.Should()
            .BeNull("no health client should be registered");

        provider
            .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .Should()
            .NotBeNull("the service clients are still registered");
    }

    [Fact]
    public void handler_chain_replaces_standard_resilience_with_a_single_grpc_pipeline()
    {
        using var provider = BuildProvider();

        var chain = GetHandlerChain(provider, GrpcClientExtensions.FlightClientName);

        chain.OfType<ResilienceHandler>().Should().ContainSingle("the host-wide standard handler must be removed");
        var primary = chain.Last().Should().BeOfType<SocketsHttpHandler>().Which;
        primary.EnableMultipleHttp2Connections.Should().BeTrue();
        primary.PooledConnectionIdleTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        primary.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(60));
        primary.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(30));
        primary.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Fact]
    public void untrusted_certificates_are_only_accepted_when_opted_in()
    {
        using var provider = BuildProvider(new() { ["Grpc:AllowUntrustedServerCertificate"] = "true" });

        var primary = GetHandlerChain(provider, GrpcClientExtensions.PassengerClientName)
            .Last()
            .Should()
            .BeOfType<SocketsHttpHandler>()
            .Which;

        primary.SslOptions.RemoteCertificateValidationCallback.Should().NotBeNull();
        primary.SslOptions.RemoteCertificateValidationCallback!(this, null, null, default).Should().BeTrue();
    }

    [Fact]
    public void add_grpc_clients_is_chainable_and_registers_both_service_clients()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:FlightAddress"] = "http://flight",
                    ["Grpc:PassengerAddress"] = "https://passenger",
                }
            )
            .Build();
        var services = new ServiceCollection();

        services.AddGrpcClients(configuration).Should().BeSameAs(services);

        services.Should().Contain(d => d.ServiceType == typeof(FlightGrpcService.FlightGrpcServiceClient));
        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(Contracts.Grpc.Passenger.PassengerGrpcService.PassengerGrpcServiceClient)
            );
    }

    private static ServiceConfig GetServiceConfig(IServiceProvider provider, string clientName)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>().Get(clientName);
        var channelOptions = new GrpcChannelOptions();
        foreach (var action in options.ChannelOptionsActions)
        {
            action(channelOptions);
        }

        channelOptions.ServiceConfig.Should().NotBeNull();
        return channelOptions.ServiceConfig!;
    }

    private static List<HttpMessageHandler> GetHandlerChain(IServiceProvider provider, string clientName)
    {
        var chain = new List<HttpMessageHandler>();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        while (handler != null)
        {
            chain.Add(handler);
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        return chain;
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?>? overrides = null,
        string address = "http://127.0.0.1:1"
    )
    {
        var settings = new Dictionary<string, string?>
        {
            ["Grpc:FlightAddress"] = address,
            ["Grpc:PassengerAddress"] = address,
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        services.AddGrpcClients(configuration);

        return services.BuildServiceProvider();
    }
}
