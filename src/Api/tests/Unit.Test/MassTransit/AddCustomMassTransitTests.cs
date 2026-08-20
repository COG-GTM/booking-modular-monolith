using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.MassTransit;

public class AddCustomMassTransitTests
{
    private static readonly Dictionary<string, string?> RabbitMqSettings = new()
    {
        ["RabbitMqOptions:HostName"] = "localhost",
        ["RabbitMqOptions:UserName"] = "guest",
        ["RabbitMqOptions:Password"] = "guest",
        ["RabbitMqOptions:Port"] = "5672",
    };

    private static IWebHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return environment;
    }

    private static IServiceCollection CreateServices(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        return services;
    }

    [Fact]
    public async Task rabbitmq_transport_in_test_environment_should_register_test_harness_and_bus()
    {
        var services = CreateServices(RabbitMqSettings);

        services.AddCustomMassTransit(
            CreateEnvironment("test"),
            TransportType.RabbitMq,
            typeof(AddCustomMassTransitTests).Assembly);

        await using var provider = services.BuildServiceProvider();

        provider.GetService<ITestHarness>().Should().NotBeNull();
        provider.GetRequiredService<IBus>().Should().NotBeNull();
    }

    [Fact]
    public async Task rabbitmq_transport_in_non_test_environment_should_register_bus_without_test_harness()
    {
        var services = CreateServices(RabbitMqSettings);

        services.AddCustomMassTransit(
            CreateEnvironment("Production"),
            TransportType.RabbitMq,
            typeof(AddCustomMassTransitTests).Assembly);

        await using var provider = services.BuildServiceProvider();

        provider.GetService<ITestHarness>().Should().BeNull();
        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
    }

    [Fact]
    public async Task rabbitmq_transport_should_register_bound_rabbitmq_options()
    {
        var services = CreateServices(RabbitMqSettings);

        services.AddCustomMassTransit(
            CreateEnvironment("Production"),
            TransportType.RabbitMq,
            typeof(AddCustomMassTransitTests).Assembly);

        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<RabbitMqOptions>();

        options.HostName.Should().Be("localhost");
        options.UserName.Should().Be("guest");
        options.Password.Should().Be("guest");
        options.Port.Should().Be(5672);
    }

    [Fact]
    public async Task rabbitmq_transport_with_aspire_connection_string_should_register_bus()
    {
        var services = CreateServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672",
        });

        services.AddCustomMassTransit(
            CreateEnvironment("Production"),
            TransportType.RabbitMq,
            typeof(AddCustomMassTransitTests).Assembly);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
    }

    [Fact]
    public async Task inmemory_transport_in_test_environment_should_register_test_harness_and_bus()
    {
        var services = CreateServices(new Dictionary<string, string?>());

        services.AddCustomMassTransit(
            CreateEnvironment("test"),
            TransportType.InMemory,
            typeof(AddCustomMassTransitTests).Assembly);

        await using var provider = services.BuildServiceProvider();

        provider.GetService<ITestHarness>().Should().NotBeNull();
        provider.GetRequiredService<IBus>().Should().NotBeNull();
    }

    [Fact]
    public void unknown_transport_type_should_throw_argument_out_of_range_exception()
    {
        var services = CreateServices(RabbitMqSettings);

        var act = () => services.AddCustomMassTransit(
            CreateEnvironment("Production"),
            (TransportType)999,
            typeof(AddCustomMassTransitTests).Assembly);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
