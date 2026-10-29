using Booking.Host.Extensions;
using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Host.Test.Composition;

public sealed class BookingReadinessCheckTests
{
    private static readonly string[] PostgresKeys =
    [
        "ConnectionStrings:booking-persist-message",
        "PersistMessageOptions:ConnectionString:Booking",
        "ConnectionStrings:booking",
        "PostgresOptions:ConnectionString:Booking",
    ];

    [Theory]
    [InlineData(null, true)]
    [InlineData("RabbitMq", true)]
    [InlineData("rabbitmq", true)]
    [InlineData("InMemory", false)]
    public void registers_only_ready_dependencies_and_includes_rabbitmq_only_for_broker_transport(
        string? transport,
        bool expectsRabbitMq
    )
    {
        using var provider = CreateProvider(Configuration(("MessagingOptions:Transport", transport)));
        var registrations = Registrations(provider);

        registrations
            .Select(registration => registration.Name)
            .Should()
            .BeEquivalentTo(
                expectsRabbitMq
                    ? new[] { "booking-postgres", "booking-mongo", "booking-eventstore", "booking-rabbitmq" }
                    : new[] { "booking-postgres", "booking-mongo", "booking-eventstore" }
            );
        foreach (var registration in registrations)
        {
            registration.Tags.Should().BeEquivalentTo(["ready"]);
            registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        }
    }

    [Theory]
    [InlineData("booking-postgres", "No Booking PostgreSQL connection string is configured.")]
    [InlineData("booking-eventstore", "No EventStoreDB connection string is configured.")]
    public async Task missing_settings_report_unhealthy_without_an_exception(string name, string description)
    {
        using var provider = CreateProvider(Configuration());

        var result = await Check(provider, name);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(description);
        result.Exception.Should().BeNull();
    }

    [Theory]
    [InlineData("ConnectionStrings:booking-persist-message")]
    [InlineData("PersistMessageOptions:ConnectionString:Booking")]
    [InlineData("ConnectionStrings:booking")]
    [InlineData("PostgresOptions:ConnectionString:Booking")]
    public async Task postgres_uses_each_supported_connection_key_and_reports_malformed_settings(string key)
    {
        using var provider = CreateProvider(Configuration((key, "not a postgres connection string")));

        var result = await Check(provider, "booking-postgres");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Booking PostgreSQL is unavailable.");
        result.Exception.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task postgres_does_not_fall_back_from_an_explicit_empty_higher_priority_connection(int priority)
    {
        var configuration = Configuration(
            PostgresKeys.Skip(priority).Select(key => (key, (string?)"not a postgres connection string")).ToArray()
        );
        configuration[PostgresKeys[priority]] = " ";
        using var provider = CreateProvider(configuration);

        var result = await Check(provider, "booking-postgres");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("No Booking PostgreSQL connection string is configured.");
        result.Exception.Should().BeNull();
    }

    [Theory]
    [InlineData("ConnectionStrings:eventstore")]
    [InlineData("EventStoreOptions:ConnectionString")]
    public async Task eventstore_uses_each_supported_connection_key_and_reports_malformed_settings(string key)
    {
        using var provider = CreateProvider(Configuration((key, "not an eventstore connection string")));

        var result = await Check(provider, "booking-eventstore");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Booking EventStoreDB is unavailable.");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task eventstore_connection_string_takes_precedence_over_legacy_options()
    {
        using var provider = CreateProvider(
            Configuration(
                ("ConnectionStrings:eventstore", " "),
                ("EventStoreOptions:ConnectionString", "not an eventstore connection string")
            )
        );

        var result = await Check(provider, "booking-eventstore");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("No EventStoreDB connection string is configured.");
        result.Exception.Should().BeNull();
    }

    [Theory]
    [InlineData("booking-postgres", "ConnectionStrings:booking-persist-message", "not a postgres connection string")]
    [InlineData("booking-eventstore", "ConnectionStrings:eventstore", "not an eventstore connection string")]
    public async Task checks_resolve_connection_settings_at_execution_time(string name, string key, string value)
    {
        var configuration = Configuration();
        using var provider = CreateProvider(configuration);
        var check = Registrations(provider).Single(registration => registration.Name == name).Factory(provider);

        var missing = await check.CheckHealthAsync(new HealthCheckContext());
        missing.Exception.Should().BeNull();

        configuration[key] = value;
        var malformed = await check.CheckHealthAsync(new HealthCheckContext());
        malformed.Status.Should().Be(HealthStatus.Unhealthy);
        malformed.Exception.Should().NotBeNull();

        configuration[key] = null;
        var removed = await check.CheckHealthAsync(new HealthCheckContext());
        removed.Description.Should().Be(missing.Description);
        removed.Exception.Should().BeNull();
    }

    [Fact]
    public async Task mongo_uses_the_booking_named_options_and_contains_configuration_errors()
    {
        var options = Substitute.For<IOptionsMonitor<MongoOptions>>();
        options
            .Get("Booking")
            .Returns(
                new MongoOptions
                {
                    ConnectionString = "not a mongo connection string",
                    DatabaseName = "booking_service_read",
                }
            );
        using var provider = CreateProvider(Configuration(), options);

        var result = await Check(provider, "booking-mongo");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Booking MongoDB is unavailable.");
        result.Exception.Should().NotBeNull();
        options.Received(1).Get("Booking");
    }

    [Fact]
    public async Task rabbitmq_contains_invalid_connection_configuration()
    {
        using var provider = CreateProvider(Configuration(("ConnectionStrings:rabbitmq", "not an amqp uri")));

        var result = await Check(provider, "booking-rabbitmq");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Booking RabbitMQ is unavailable.");
        result.Exception.Should().BeOfType<UriFormatException>();
    }

    private static IConfigurationRoot Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();

    private static ServiceProvider CreateProvider(
        IConfiguration configuration,
        IOptionsMonitor<MongoOptions>? mongoOptions = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        if (mongoOptions is not null)
            services.AddSingleton(mongoOptions);
        services.AddBookingHostHealthChecks(configuration);
        return services.BuildServiceProvider();
    }

    private static ICollection<HealthCheckRegistration> Registrations(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

    private static Task<HealthCheckResult> Check(IServiceProvider provider, string name)
    {
        var registration = Registrations(provider).Single(candidate => candidate.Name == name);
        return registration.Factory(provider).CheckHealthAsync(new HealthCheckContext { Registration = registration });
    }
}
