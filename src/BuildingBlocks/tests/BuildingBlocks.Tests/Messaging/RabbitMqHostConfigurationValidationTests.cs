#pragma warning disable SA1300

using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class RabbitMqHostConfigurationValidationTests
{
    [Fact]
    public void whitespace_host_name_without_connection_string_is_rejected()
    {
        var values = new Dictionary<string, string?> { ["RabbitMqOptions:HostName"] = " \t " };
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(values);
        var configuration = builder.Build();
        var configurator = Substitute.For<IRabbitMqBusFactoryConfigurator>();

        var act = () => Extensions.ConfigureRabbitMqHost(configurator, configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:rabbitmq*RabbitMqOptions:HostName*");
    }
}
