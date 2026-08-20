using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Unit.Test.MassTransit;

public class ApiAppSettingsRabbitMqTests
{
    private static IConfiguration LoadApiSettings(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiSettings", fileName);

        return new ConfigurationBuilder().AddJsonFile(path).Build();
    }

    [Fact]
    public void api_appsettings_should_contain_complete_rabbitmq_options()
    {
        var configuration = LoadApiSettings("appsettings.json");

        var options = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        options.HostName.Should().Be("localhost");
        options.UserName.Should().NotBeNullOrWhiteSpace();
        options.Password.Should().NotBeNullOrWhiteSpace();
        options.Port.Should().Be(5672);
    }

    [Fact]
    public void api_docker_appsettings_should_contain_complete_rabbitmq_options()
    {
        var configuration = LoadApiSettings("appsettings.docker.json");

        var options = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        options.HostName.Should().Be("rabbitmq");
        options.UserName.Should().NotBeNullOrWhiteSpace();
        options.Password.Should().NotBeNullOrWhiteSpace();
        options.Port.Should().Be(5672);
    }
}
