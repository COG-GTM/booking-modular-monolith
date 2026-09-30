using System.Net.Security;
using BuildingBlocks.Grpc;
using Contracts.Grpc.Flight.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Grpc;

public class ResilientGrpcClientTests
{
    private const string ClientName = "flight";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_missing_address(string? address)
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddResilientGrpcClient<FlightGrpcService.FlightGrpcServiceClient>(
                ClientName,
                new GrpcClientOptions { Address = address! }
            );

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_configure_retries_on_unavailable_from_options()
    {
        var options = new GrpcClientOptions
        {
            Address = "https://flight",
            MaxAttempts = 4,
            InitialBackoff = TimeSpan.FromMilliseconds(50),
            MaxBackoff = TimeSpan.FromSeconds(1),
        };

        var channelOptions = BuildChannelOptions(options);

        var methodConfig = channelOptions.ServiceConfig!.MethodConfigs.Should().ContainSingle().Subject;
        methodConfig.Names.Should().ContainSingle().Which.Should().Be(MethodName.Default);

        var retryPolicy = methodConfig.RetryPolicy;
        retryPolicy.Should().NotBeNull();
        retryPolicy!.MaxAttempts.Should().Be(4);
        retryPolicy.InitialBackoff.Should().Be(TimeSpan.FromMilliseconds(50));
        retryPolicy.MaxBackoff.Should().Be(TimeSpan.FromSeconds(1));
        retryPolicy.BackoffMultiplier.Should().Be(2);
        retryPolicy.RetryableStatusCodes.Should().BeEquivalentTo([StatusCode.Unavailable]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void should_not_configure_retries_when_max_attempts_is_one_or_less(int maxAttempts)
    {
        var channelOptions = BuildChannelOptions(
            new GrpcClientOptions { Address = "https://flight", MaxAttempts = maxAttempts }
        );

        var methodConfig = channelOptions.ServiceConfig!.MethodConfigs.Should().ContainSingle().Subject;
        methodConfig.RetryPolicy.Should().BeNull();
    }

    [Fact]
    public void should_register_deadline_interceptor_per_client()
    {
        var provider = BuildProvider(new GrpcClientOptions { Address = "https://flight" });

        var registrations = provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get(ClientName)
            .InterceptorRegistrations;

        registrations
            .Should()
            .ContainSingle(r => r.Creator(provider) is GrpcClientDeadlineInterceptor)
            .Which.Scope.Should()
            .Be(InterceptorScope.Channel);
    }

    [Fact]
    public void should_use_http2_handler_with_tls_validation_by_default()
    {
        var handler = ResolvePrimaryHandler(new GrpcClientOptions { Address = "https://flight" });

        handler.EnableMultipleHttp2Connections.Should().BeTrue();
        handler.PooledConnectionIdleTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        handler.KeepAlivePingDelay.Should().Be(TimeSpan.FromSeconds(60));
        handler.KeepAlivePingTimeout.Should().Be(TimeSpan.FromSeconds(30));
        handler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Fact]
    public void should_accept_any_server_certificate_only_when_opted_in()
    {
        var handler = ResolvePrimaryHandler(
            new GrpcClientOptions { Address = "https://flight", AcceptAnyServerCertificate = true }
        );

        var callback = handler.SslOptions.RemoteCertificateValidationCallback;
        callback.Should().NotBeNull();
        callback!(this, null, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue();
    }

    [Fact]
    public void should_register_dependency_health_check_with_dedicated_health_client()
    {
        var options = new GrpcClientOptions { Address = "https://flight", Deadline = TimeSpan.FromSeconds(3) };
        var services = CreateServices();

        services.AddHealthChecks().AddGrpcServiceHealthCheck(ClientName, options);

        var provider = services.BuildServiceProvider();

        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .ContainSingle()
            .Subject;
        registration.Name.Should().Be(ClientName);
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        registration.Timeout.Should().Be(TimeSpan.FromSeconds(3));
        registration.Tags.Should().BeEquivalentTo(Extensions.ReadinessTag, Extensions.GrpcDependencyTag);
        registration.Factory(provider).Should().BeOfType<GrpcServiceHealthCheck>();

        var healthClientOptions = provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get($"{ClientName}-health");
        healthClientOptions.Address.Should().Be(new Uri("https://flight"));
        healthClientOptions.InterceptorRegistrations.Should().ContainSingle();

        provider
            .GetRequiredService<GrpcClientFactory>()
            .CreateClient<Health.HealthClient>($"{ClientName}-health")
            .Should()
            .NotBeNull();
    }

    private static GrpcChannelOptions BuildChannelOptions(GrpcClientOptions options)
    {
        var provider = BuildProvider(options);
        var channelOptions = new GrpcChannelOptions();

        foreach (
            var configure in provider
                .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
                .Get(ClientName)
                .ChannelOptionsActions
        )
        {
            configure(channelOptions);
        }

        return channelOptions;
    }

    private static SocketsHttpHandler ResolvePrimaryHandler(GrpcClientOptions options)
    {
        var provider = BuildProvider(options);
        HttpMessageHandler handler = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(ClientName);

        while (handler is DelegatingHandler { InnerHandler: not null } delegating)
        {
            handler = delegating.InnerHandler;
        }

        return handler.Should().BeOfType<SocketsHttpHandler>().Subject;
    }

    private static ServiceProvider BuildProvider(GrpcClientOptions options)
    {
        var services = CreateServices();
        services.AddResilientGrpcClient<FlightGrpcService.FlightGrpcServiceClient>(ClientName, options);

        return services.BuildServiceProvider();
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        return services;
    }
}
