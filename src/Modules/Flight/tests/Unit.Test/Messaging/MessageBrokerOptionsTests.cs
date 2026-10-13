namespace Unit.Test.Messaging;

using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Xunit;

public class MessageBrokerOptionsTests
{
    [Fact]
    public void defaults_should_be_in_memory_without_inbox_and_local_guest_rabbitmq()
    {
        var options = new ConfigurationBuilder()
            .Build()
            .GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);

        options.TransportType.Should().Be(TransportType.InMemory);
        options.UseInbox.Should().BeFalse();
        options.ServiceName.Should().BeNull();
        options.RabbitMq.Should().NotBeNull();
        options.RabbitMq.Host.Should().Be("localhost");
        options.RabbitMq.Port.Should().Be(5672);
        options.RabbitMq.VirtualHost.Should().Be("/");
        options.RabbitMq.Username.Should().Be("guest");
        options.RabbitMq.Password.Should().Be("guest");
    }

    [Fact]
    public void should_bind_the_message_broker_section_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["MessageBroker:TransportType"] = "RabbitMq",
                    ["MessageBroker:ServiceName"] = "booking-monolith",
                    ["MessageBroker:UseInbox"] = "true",
                    ["MessageBroker:RabbitMq:Host"] = "rabbitmq",
                    ["MessageBroker:RabbitMq:Port"] = "5673",
                    ["MessageBroker:RabbitMq:VirtualHost"] = "/booking",
                    ["MessageBroker:RabbitMq:Username"] = "svc",
                    ["MessageBroker:RabbitMq:Password"] = "secret",
                }
            )
            .Build();

        var options = configuration.GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);

        options.TransportType.Should().Be(TransportType.RabbitMq);
        options.ServiceName.Should().Be("booking-monolith");
        options.UseInbox.Should().BeTrue();
        options.RabbitMq.Host.Should().Be("rabbitmq");
        options.RabbitMq.Port.Should().Be(5673);
        options.RabbitMq.VirtualHost.Should().Be("/booking");
        options.RabbitMq.Username.Should().Be("svc");
        options.RabbitMq.Password.Should().Be("secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    public void endpoint_name_formatter_without_service_name_should_be_the_plain_kebab_case_formatter(
        string? serviceName
    )
    {
        var formatter = Extensions.CreateEndpointNameFormatter(new MessageBrokerOptions { ServiceName = serviceName });

        formatter.Should().BeSameAs(KebabCaseEndpointNameFormatter.Instance);
        formatter.Consumer<RegisterNewUserHandler>().Should().Be("register-new-user-handler");
    }

    [Theory]
    [InlineData("passenger-it", "passenger-it-register-new-user-handler")]
    [InlineData("booking-monolith", "booking-monolith-register-new-user-handler")]
    [InlineData(" identity- ", "identity-register-new-user-handler")]
    [InlineData("-flight-", "flight-register-new-user-handler")]
    public void endpoint_name_formatter_should_prefix_queue_names_with_the_normalized_service_name(
        string serviceName,
        string expectedQueue
    )
    {
        var formatter = Extensions.CreateEndpointNameFormatter(new MessageBrokerOptions { ServiceName = serviceName });

        formatter.Should().NotBeSameAs(KebabCaseEndpointNameFormatter.Instance);
        formatter.Consumer<RegisterNewUserHandler>().Should().Be(expectedQueue);
    }

    [Fact]
    public void endpoint_name_formatter_should_not_include_the_consumer_namespace()
    {
        var formatter = Extensions.CreateEndpointNameFormatter(new MessageBrokerOptions { ServiceName = "passenger" });

        formatter.Consumer<RegisterNewUserHandler>().Should().NotContain("unit-test").And.NotContain("Unit.Test");
    }

    public record UserCreated(Guid Id);

    public class RegisterNewUserHandler : IConsumer<UserCreated>
    {
        public Task Consume(ConsumeContext<UserCreated> context) => Task.CompletedTask;
    }
}
