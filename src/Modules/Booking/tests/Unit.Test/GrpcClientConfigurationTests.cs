using Booking.Extensions.Infrastructure;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Xunit;
using FlightGrpcService = Contracts.Grpc.Flight.FlightGrpcService;
using PassengerGrpcService = Contracts.Grpc.Passenger.PassengerGrpcService;

namespace Booking.Unit.Test;

public class GrpcClientConfigurationTests
{
    [Theory]
    [InlineData(null, "<missing>")]
    [InlineData("relative/path", null)]
    [InlineData("ftp://p.example", null)]
    public void missing_or_invalid_passenger_address_names_the_configuration_key(
        string? passengerAddress,
        string? expectedValue
    )
    {
        var settings = new Dictionary<string, string?> { ["Grpc:FlightAddress"] = "http://flight.example:81" };
        if (passengerAddress is not null)
        {
            settings["Grpc:PassengerAddress"] = passengerAddress;
        }

        var exception = Assert.Throws<InvalidOperationException>(() => CreateProvider(settings));

        exception.Message.Should().Contain("Grpc:PassengerAddress");
        if (expectedValue is not null)
        {
            exception.Message.Should().Contain(expectedValue);
        }
    }

    [Fact]
    public void missing_grpc_section_names_the_flight_address()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(new Dictionary<string, string?>())
        );

        exception.Message.Should().Contain("Grpc:FlightAddress").And.Contain("<missing>");
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void deadline_must_be_greater_than_zero(string deadline)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(Settings(("Grpc:Deadline", deadline)))
        );

        exception.Message.Should().Be("Grpc:Deadline must be greater than zero.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    public void retry_attempts_outside_the_supported_range_are_rejected(string maxRetryAttempts)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(Settings(("Grpc:MaxRetryAttempts", maxRetryAttempts)))
        );

        exception.Message.Should().Contain("between 1 and 5");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    public void retry_attempts_at_the_supported_boundaries_are_accepted(string maxRetryAttempts)
    {
        using var provider = CreateProvider(Settings(("Grpc:MaxRetryAttempts", maxRetryAttempts)));

        provider.Should().NotBeNull();
    }

    [Fact]
    public void max_retry_attempts_of_one_disables_retry_policies_for_both_clients()
    {
        using var provider = CreateProvider(Settings(("Grpc:MaxRetryAttempts", "1")));
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();

        foreach (
            var clientName in new[]
            {
                typeof(FlightGrpcService.FlightGrpcServiceClient).Name,
                typeof(PassengerGrpcService.PassengerGrpcServiceClient).Name,
            }
        )
        {
            var clientOptions = options.Get(clientName);
            var channelOptions = new GrpcChannelOptions();
            foreach (var configureChannel in clientOptions.ChannelOptionsActions)
            {
                configureChannel(channelOptions);
            }

            channelOptions.ServiceConfig.Should().NotBeNull();
            channelOptions.ServiceConfig!.MethodConfigs.Should().BeEmpty();
        }
    }

    [Fact]
    public void retry_policies_cover_only_idempotent_reads_and_use_the_defaults()
    {
        using var provider = CreateProvider();

        AssertRetryPolicy(provider, typeof(FlightGrpcService.FlightGrpcServiceClient).Name, 3, 200, 2);
        AssertRetryPolicy(provider, typeof(PassengerGrpcService.PassengerGrpcServiceClient).Name, 3, 200, 2);
    }

    [Fact]
    public void retry_policies_use_configured_attempts_and_backoffs()
    {
        using var provider = CreateProvider(
            Settings(
                ("Grpc:MaxRetryAttempts", "5"),
                ("Grpc:InitialBackoff", "00:00:00.050"),
                ("Grpc:MaxBackoff", "00:00:01")
            )
        );

        AssertRetryPolicy(provider, typeof(FlightGrpcService.FlightGrpcServiceClient).Name, 5, 50, 1);
        AssertRetryPolicy(provider, typeof(PassengerGrpcService.PassengerGrpcServiceClient).Name, 5, 50, 1);
    }

    [Fact]
    public void both_clients_register_a_deadline_interceptor()
    {
        using var provider = CreateProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();

        foreach (
            var clientName in new[]
            {
                typeof(FlightGrpcService.FlightGrpcServiceClient).Name,
                typeof(PassengerGrpcService.PassengerGrpcServiceClient).Name,
            }
        )
        {
            var registrations = options.Get(clientName).InterceptorRegistrations;
            registrations.Should().ContainSingle();
            registrations.Single().Creator(provider).Should().BeOfType<GrpcDeadlineInterceptor>();
        }
    }

    [Fact]
    public void grpc_clients_remove_standard_resilience_handlers()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddHttpClient("control");
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrpcClients(configuration);

        using var provider = services.BuildServiceProvider();
        var handlerFactory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var clientNames = new[]
        {
            typeof(FlightGrpcService.FlightGrpcServiceClient).Name,
            typeof(PassengerGrpcService.PassengerGrpcServiceClient).Name,
            "flight-health",
            "passenger-health",
        };

        foreach (var clientName in clientNames)
        {
            ContainsResilienceHandler(handlerFactory.CreateHandler(clientName)).Should().BeFalse(clientName);
        }

        ContainsResilienceHandler(handlerFactory.CreateHandler("control")).Should().BeTrue();
    }

    [Fact]
    public void health_checks_are_registered_as_ready_by_default()
    {
        using var provider = CreateProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Should().Contain(registration => registration.Name == "grpc-flight");
        registrations.Should().Contain(registration => registration.Name == "grpc-passenger");
        var grpcRegistrations = registrations
            .Where(registration => registration.Name is "grpc-flight" or "grpc-passenger")
            .ToArray();
        grpcRegistrations.Should().HaveCount(2);
        grpcRegistrations
            .Select(registration => registration.Tags)
            .Should()
            .AllSatisfy(tags => tags.Should().BeEquivalentTo(new[] { "ready" }));
    }

    [Fact]
    public void disabling_health_checks_skips_their_registration()
    {
        using var provider = CreateProvider(Settings(("Grpc:HealthChecksEnabled", "false")));
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations
            .Should()
            .NotContain(registration => registration.Name == "grpc-flight" || registration.Name == "grpc-passenger");
    }

    [Fact]
    public void plain_dns_addresses_are_passed_through_unchanged()
    {
        const string address = "http://flight-service:81";
        using var provider = CreateProvider(Settings(("Grpc:FlightAddress", address)));
        var options = provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get(typeof(FlightGrpcService.FlightGrpcServiceClient).Name);

        options.Address.Should().Be(new Uri(address));
    }

    private static void AssertRetryPolicy(
        ServiceProvider provider,
        string clientName,
        int maxAttempts,
        int initialBackoffMilliseconds,
        int maxBackoffSeconds
    )
    {
        var clientOptions = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>().Get(clientName);
        var channelOptions = new GrpcChannelOptions();
        foreach (var configureChannel in clientOptions.ChannelOptionsActions)
        {
            configureChannel(channelOptions);
        }

        var serviceConfig = channelOptions.ServiceConfig;
        serviceConfig.Should().NotBeNull();
        var methodNames = serviceConfig!.MethodConfigs.SelectMany(methodConfig =>
            methodConfig.Names.Select(methodName => $"{methodName.Service}/{methodName.Method}")
        );
        methodNames
            .Should()
            .BeEquivalentTo(
                "flight.FlightGrpcService/GetById",
                "flight.FlightGrpcService/GetAvailableSeats",
                "passenger.PassengerGrpcService/GetById"
            );
        methodNames.Should().NotContain("flight.FlightGrpcService/ReserveSeat");

        foreach (var methodConfig in serviceConfig.MethodConfigs)
        {
            methodConfig.RetryPolicy.Should().NotBeNull();
            var retryPolicy = methodConfig.RetryPolicy!;
            retryPolicy.RetryableStatusCodes.Should().Equal(StatusCode.Unavailable);
            retryPolicy.BackoffMultiplier.Should().Be(2);
            retryPolicy.MaxAttempts.Should().Be(maxAttempts);
            retryPolicy.InitialBackoff.Should().Be(TimeSpan.FromMilliseconds(initialBackoffMilliseconds));
            retryPolicy.MaxBackoff.Should().Be(TimeSpan.FromSeconds(maxBackoffSeconds));
        }
    }

    private static bool ContainsResilienceHandler(HttpMessageHandler handler)
    {
        for (
            HttpMessageHandler? current = handler;
            current is not null;
            current = (current as DelegatingHandler)?.InnerHandler
        )
        {
            if (current.GetType().Name == "ResilienceHandler")
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string?> Settings(params (string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Grpc:FlightAddress"] = "http://flight.example:81",
            ["Grpc:PassengerAddress"] = "http://passenger.example:81",
        };
        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?>? settings = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings ?? Settings()).Build();

    private static ServiceProvider CreateProvider(IDictionary<string, string?>? settings = null)
    {
        var configuration = BuildConfiguration(settings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGrpcClients(configuration);
        return services.BuildServiceProvider();
    }
}
