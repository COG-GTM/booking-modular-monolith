using System.Text;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Unit.Test.MassTransit;

public class RabbitMqOptionsBindingTests
{
    [Fact]
    public void rabbitmq_options_section_should_bind_all_properties_from_json_configuration()
    {
        const string json = """
        {
            "RabbitMqOptions": {
                "HostName": "localhost",
                "UserName": "guest",
                "Password": "guest",
                "Port": 5672
            }
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var options = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        options.HostName.Should().Be("localhost");
        options.UserName.Should().Be("guest");
        options.Password.Should().Be("guest");
        options.Port.Should().Be(5672);
    }

    [Fact]
    public void rabbitmq_options_with_missing_port_should_bind_null_port()
    {
        const string json = """
        {
            "RabbitMqOptions": {
                "HostName": "rabbitmq",
                "UserName": "guest",
                "Password": "guest"
            }
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var options = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        options.HostName.Should().Be("rabbitmq");
        options.Port.Should().BeNull();
    }
}
