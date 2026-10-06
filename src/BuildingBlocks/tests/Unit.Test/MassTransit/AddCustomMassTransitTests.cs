namespace Unit.Test.MassTransit;

using global::MassTransit;
using BuildingBlocks.MassTransit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Unit.Test.Common;
using Unit.Test.MassTransit.Fakes;
using Xunit;

public class AddCustomMassTransitTests
{
    private static ServiceProvider BuildProvider(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(x => x.Key, x => x.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<ReceiveEndpointNameRecorder>();
        services.AddSingleton<IConfigureReceiveEndpoint>(sp => sp.GetRequiredService<ReceiveEndpointNameRecorder>());

        services.AddCustomMassTransit(new FakeWebHostEnvironment(), typeof(FakeUserCreatedConsumer).Assembly);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task default_overload_should_use_rabbitmq_host_from_aspire_connection_string()
    {
        await using var provider = BuildProvider(
            ("ConnectionStrings:rabbitmq", "amqp://guest:guest@aspire-rabbit:5672")
        );

        var bus = provider.GetRequiredService<IBus>();

        bus.Address.Scheme.Should().Be("rabbitmq");
        bus.Address.Host.Should().Be("aspire-rabbit");
    }

    [Fact]
    public async Task default_overload_should_fall_back_to_rabbitmq_options_when_no_connection_string_is_set()
    {
        await using var provider = BuildProvider(
            ("RabbitMqOptions:HostName", "options-rabbit"),
            ("RabbitMqOptions:Port", "5673"),
            ("RabbitMqOptions:UserName", "guest"),
            ("RabbitMqOptions:Password", "guest")
        );

        var bus = provider.GetRequiredService<IBus>();

        bus.Address.Scheme.Should().Be("rabbitmq");
        bus.Address.Host.Should().Be("options-rabbit");
        bus.Address.Port.Should().Be(5673);
    }

    [Fact]
    public async Task default_overload_should_prefer_aspire_connection_string_over_rabbitmq_options()
    {
        await using var provider = BuildProvider(
            ("ConnectionStrings:rabbitmq", "amqp://guest:guest@aspire-rabbit:5672"),
            ("RabbitMqOptions:HostName", "options-rabbit")
        );

        var bus = provider.GetRequiredService<IBus>();

        bus.Address.Host.Should().Be("aspire-rabbit");
    }

    [Fact]
    public async Task default_overload_should_throw_when_neither_connection_string_nor_host_name_is_configured()
    {
        await using var provider = BuildProvider(("AppOptions:Name", "Flight-Api"));

        var act = () => provider.GetRequiredService<IBus>();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task default_overload_should_throw_when_rabbitmq_host_name_is_blank()
    {
        await using var provider = BuildProvider(("RabbitMqOptions:HostName", " "));

        var act = () => provider.GetRequiredService<IBus>();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("custom-prefix", null, "custom-prefix-fake-user-created")]
    [InlineData("custom-prefix", "Passenger-Api", "custom-prefix-fake-user-created")]
    [InlineData(null, "Passenger-Api", "passenger-api-fake-user-created")]
    [InlineData(null, "Booking-Modular-Monolith", "booking-modular-monolith-fake-user-created")]
    [InlineData(" ", "FlightApi", "flight-api-fake-user-created")]
    [InlineData(null, null, "service-fake-user-created")]
    [InlineData(null, " ", "service-fake-user-created")]
    public async Task receive_endpoint_names_should_be_prefixed_with_queue_prefix_or_kebab_cased_app_name(
        string? queuePrefix,
        string? appName,
        string expectedQueueName
    )
    {
        await using var provider = BuildProvider(
            ("ConnectionStrings:rabbitmq", "amqp://guest:guest@aspire-rabbit:5672"),
            ("RabbitMqOptions:QueuePrefix", queuePrefix),
            ("AppOptions:Name", appName)
        );

        _ = provider.GetRequiredService<IBus>();

        provider.GetRequiredService<ReceiveEndpointNameRecorder>().Names.Should().ContainSingle(expectedQueueName);
    }
}
