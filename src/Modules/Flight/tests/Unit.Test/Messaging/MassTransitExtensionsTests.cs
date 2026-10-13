using System.Linq.Expressions;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Messaging;

public record BrokerProbeMessage(Guid Id);

public class BrokerProbeConsumer : IConsumer<BrokerProbeMessage>
{
    public Task Consume(ConsumeContext<BrokerProbeMessage> context) => Task.CompletedTask;
}

public class MassTransitExtensionsTests
{
    private readonly IPersistMessageProcessor _processor = Substitute.For<IPersistMessageProcessor>();

    public MassTransitExtensionsTests()
    {
        _processor
            .GetByFilterAsync(Arg.Any<Expression<Func<PersistMessage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PersistMessage>().AsReadOnly());
    }

    [Fact]
    public void message_broker_options_should_default_to_in_memory_with_the_inbox_enabled()
    {
        var options = new MessageBrokerOptions();

        options.TransportType.Should().Be(TransportType.InMemory);
        options.UseInbox.Should().BeTrue();
        options.ServiceName.Should().BeNull();
    }

    [Fact]
    public void configuration_overload_should_bind_message_broker_options_from_the_configuration_section()
    {
        var services = CreateServices(
            new Dictionary<string, string?>
            {
                ["MessageBrokerOptions:TransportType"] = "InMemory",
                ["MessageBrokerOptions:ServiceName"] = "flight-service",
                ["MessageBrokerOptions:UseInbox"] = "false",
            },
            out var configuration
        );

        services.AddCustomMassTransit(configuration, TestEnvironment(), typeof(BrokerProbeConsumer).Assembly);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessageBrokerOptions>();
        options.TransportType.Should().Be(TransportType.InMemory);
        options.ServiceName.Should().Be("flight-service");
        options.UseInbox.Should().BeFalse();
    }

    [Fact]
    public void service_name_should_prefix_every_receive_endpoint_with_its_kebab_case_form()
    {
        var services = CreateServices(
            new Dictionary<string, string?> { ["MessageBrokerOptions:ServiceName"] = "FlightService" },
            out var configuration
        );

        services.AddCustomMassTransit(configuration, TestEnvironment(), typeof(BrokerProbeConsumer).Assembly);

        using var provider = services.BuildServiceProvider();
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();
        formatter.Should().BeOfType<KebabCaseEndpointNameFormatter>();
        formatter.Consumer<BrokerProbeConsumer>().Should().StartWith("flight-service");
        formatter.Consumer<BrokerProbeConsumer>().Should().Contain("broker-probe");
    }

    [Fact]
    public void monolith_overload_should_not_register_a_service_scoped_endpoint_name_formatter()
    {
        var services = CreateServices(new Dictionary<string, string?>(), out _);

        services.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory, typeof(BrokerProbeConsumer).Assembly);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IEndpointNameFormatter>().Should().BeNull();
    }

    [Fact]
    public async Task service_host_bus_with_inbox_enabled_should_persist_consumed_messages_under_their_message_id()
    {
        var services = CreateServices(
            new Dictionary<string, string?>
            {
                ["MessageBrokerOptions:TransportType"] = "InMemory",
                ["MessageBrokerOptions:ServiceName"] = "flight-service",
                ["MessageBrokerOptions:UseInbox"] = "true",
            },
            out var configuration
        );
        services.AddCustomMassTransit(configuration, TestEnvironment(), typeof(BrokerProbeConsumer).Assembly);
        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        try
        {
            await harness.Bus.Publish(new BrokerProbeMessage(NewId.NextGuid()));

            (await harness.Consumed.Any<BrokerProbeMessage>()).Should().BeTrue();
            var consumed = await harness.Consumed.SelectAsync<BrokerProbeMessage>().First();
            await _processor
                .Received(1)
                .AddReceivedMessageAsync(
                    Arg.Is<MessageEnvelope>(e => e.Message is BrokerProbeMessage),
                    consumed.Context.MessageId,
                    Arg.Any<CancellationToken>()
                );
            await _processor
                .Received(1)
                .ProcessInboxAsync(consumed.Context.MessageId!.Value, Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task monolith_bus_should_consume_without_touching_the_inbox()
    {
        var services = CreateServices(new Dictionary<string, string?>(), out _);
        services.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory, typeof(BrokerProbeConsumer).Assembly);
        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        try
        {
            await harness.Bus.Publish(new BrokerProbeMessage(NewId.NextGuid()));

            (await harness.Consumed.Any<BrokerProbeMessage>()).Should().BeTrue();
            await _processor
                .DidNotReceive()
                .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
            await _processor.DidNotReceive().ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public void empty_assembly_list_should_not_scan_the_app_domain_for_consumers()
    {
        var withAssembly = CreateServices(new Dictionary<string, string?>(), out var configuration);
        withAssembly.AddCustomMassTransit(configuration, TestEnvironment(), typeof(BrokerProbeConsumer).Assembly);

        var withoutAssembly = CreateServices(new Dictionary<string, string?>(), out configuration);
        withoutAssembly.AddCustomMassTransit(configuration, TestEnvironment());

        withAssembly.Should().Contain(d => d.ServiceType == typeof(BrokerProbeConsumer));
        withoutAssembly.Should().NotContain(d => d.ServiceType == typeof(BrokerProbeConsumer));
    }

    private IServiceCollection CreateServices(IDictionary<string, string?> settings, out IConfiguration configuration)
    {
        configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddScoped(_ => _processor);

        return services;
    }

    private static IWebHostEnvironment TestEnvironment()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("test");
        return env;
    }
}
