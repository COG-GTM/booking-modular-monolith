using BuildingBlocks.MassTransit;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Unit.Test.MassTransit;

public class RabbitMqOptionsResolutionTests
{
    private static IServiceCollection CreateServicesWithMassTransit(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");

        services.AddCustomMassTransit(
            environment,
            TransportType.RabbitMq,
            typeof(RabbitMqOptionsResolutionTests).Assembly
        );

        return services;
    }

    [Fact]
    public async Task ioptions_rabbitmq_options_should_resolve_with_bound_configuration_values()
    {
        var services = CreateServicesWithMassTransit(
            new Dictionary<string, string?>
            {
                ["RabbitMqOptions:HostName"] = "broker-host",
                ["RabbitMqOptions:UserName"] = "broker-user",
                ["RabbitMqOptions:Password"] = "broker-password",
                ["RabbitMqOptions:Port"] = "5673",
            }
        );

        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        options.HostName.Should().Be("broker-host");
        options.UserName.Should().Be("broker-user");
        options.Password.Should().Be("broker-password");
        options.Port.Should().Be(5673);
    }

    [Fact]
    public async Task ioptions_rabbitmq_options_should_resolve_same_instance_as_singleton_registration()
    {
        var services = CreateServicesWithMassTransit(
            new Dictionary<string, string?>
            {
                ["RabbitMqOptions:HostName"] = "localhost",
                ["RabbitMqOptions:UserName"] = "guest",
                ["RabbitMqOptions:Password"] = "guest",
                ["RabbitMqOptions:Port"] = "5672",
            }
        );

        await using var provider = services.BuildServiceProvider();

        var optionsValue = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        var singleton = provider.GetRequiredService<RabbitMqOptions>();

        singleton.Should().BeSameAs(optionsValue);
    }

    [Fact]
    public async Task ioptions_rabbitmq_options_without_configuration_section_should_resolve_empty_options()
    {
        var services = CreateServicesWithMassTransit(new Dictionary<string, string?>());

        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        options.HostName.Should().BeNull();
        options.UserName.Should().BeNull();
        options.Password.Should().BeNull();
        options.Port.Should().BeNull();
    }
}
