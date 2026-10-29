using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class MassTransitTransportTests
{
    [Fact]
    public void transport_defaults_to_rabbitmq_when_not_configured()
    {
        Extensions.ResolveTransport(Configuration()).Should().Be(TransportType.RabbitMq);
    }

    [Theory]
    [InlineData("RabbitMq", TransportType.RabbitMq)]
    [InlineData("rabbitmq", TransportType.RabbitMq)]
    [InlineData("InMemory", TransportType.InMemory)]
    [InlineData("inmemory", TransportType.InMemory)]
    public void transport_is_read_from_messaging_options(string configured, TransportType expected)
    {
        Extensions.ResolveTransport(Configuration(("MessagingOptions:Transport", configured))).Should().Be(expected);
    }

    [Theory]
    [InlineData("Kafka")]
    [InlineData("7")]
    public void unknown_transport_fails_fast(string configured)
    {
        var act = () => Extensions.ResolveTransport(Configuration(("MessagingOptions:Transport", configured)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*RabbitMq, InMemory*");
    }

    [Fact]
    public void consumer_queue_is_prefixed_with_the_owning_module()
    {
        ModuleEndpointNameFormatter
            .Instance.Consumer<SampleRegistrationConsumer>()
            .Should()
            .Be("buildingblocks-tests.sample-registration");
    }

    [Fact]
    public void module_name_is_derived_from_the_assembly_of_the_consumer()
    {
        ModuleEndpointNameFormatter
            .ModuleName(typeof(ModuleEndpointNameFormatter))
            .Should()
            .Be("buildingblocks-masstransit");
    }

    [Fact]
    public void rabbitmq_host_requires_a_connection_string_or_host_name()
    {
        var act = () => Extensions.ConfigureRabbitMqHost(null!, Configuration());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:rabbitmq*RabbitMqOptions:HostName*");
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class SampleRegistrationConsumer : IConsumer<SampleMessage>
    {
        public Task Consume(ConsumeContext<SampleMessage> context) => Task.CompletedTask;
    }

    private sealed record SampleMessage;
}
