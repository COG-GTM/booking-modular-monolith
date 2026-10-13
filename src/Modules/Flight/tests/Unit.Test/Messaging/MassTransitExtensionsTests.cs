namespace Unit.Test.Messaging;

using System.Linq.Expressions;
using System.Reflection;
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

public class MassTransitExtensionsTests
{
    private static readonly Assembly TestAssembly = typeof(MassTransitExtensionsTests).Assembly;

    private static IWebHostEnvironment TestEnvironment()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("test");
        return env;
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceCollection CreateServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<ConsumedAddresses>();
        return services;
    }

    private static IPersistMessageProcessor EmptyInbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        processor
            .GetByFilterAsync(Arg.Any<Expression<Func<PersistMessage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PersistMessage>().AsReadOnly());
        return processor;
    }

    [Fact]
    public async Task configuration_overload_should_read_transport_type_from_the_message_broker_section()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["MessageBroker:TransportType"] = "RabbitMq",
                ["MessageBroker:ServiceName"] = "booking-monolith",
                ["MessageBroker:UseInbox"] = "true",
            }
        );
        var services = CreateServices(configuration);

        services.AddCustomMassTransit(configuration, TestEnvironment(), TestAssembly);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessageBrokerOptions>();
        options.TransportType.Should().Be(TransportType.RabbitMq);
        options.ServiceName.Should().Be("booking-monolith");
        options.UseInbox.Should().BeTrue();
        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
    }

    [Fact]
    public async Task configuration_overload_should_default_to_in_memory_when_the_section_is_missing()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var services = CreateServices(configuration);

        services.AddCustomMassTransit(configuration, TestEnvironment(), TestAssembly);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessageBrokerOptions>();
        options.TransportType.Should().Be(TransportType.InMemory);
        options.UseInbox.Should().BeFalse();
        options.ServiceName.Should().BeNull();
    }

    [Fact]
    public async Task connection_settings_should_be_read_from_the_final_configuration_when_the_bus_is_built()
    {
        var registrationTime = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["MessageBroker:TransportType"] = "InMemory",
                ["MessageBroker:ServiceName"] = "registration-time",
            }
        );
        var finalConfiguration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["MessageBroker:TransportType"] = "RabbitMq",
                ["MessageBroker:ServiceName"] = "late-override",
                ["MessageBroker:UseInbox"] = "true",
                ["MessageBroker:RabbitMq:Host"] = "late-host",
            }
        );
        var services = CreateServices(finalConfiguration);

        services.AddCustomMassTransit(registrationTime, TestEnvironment(), TestAssembly);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessageBrokerOptions>();
        options.TransportType.Should().Be(TransportType.InMemory, "only the transport is fixed at registration");
        options.ServiceName.Should().Be("late-override");
        options.UseInbox.Should().BeTrue();
        options.RabbitMq.Host.Should().Be("late-host");
        provider
            .GetRequiredService<IEndpointNameFormatter>()
            .Consumer<WiringHandler>()
            .Should()
            .Be("late-override-wiring-handler");
    }

    [Fact]
    public async Task explicit_options_overload_should_register_the_given_options_and_a_prefixed_endpoint_name_formatter()
    {
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));
        var options = new MessageBrokerOptions { TransportType = TransportType.InMemory, ServiceName = "flight" };

        services.AddCustomMassTransit(TestEnvironment(), options, TestAssembly);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MessageBrokerOptions>().ServiceName.Should().Be("flight");
        provider
            .GetRequiredService<IEndpointNameFormatter>()
            .Consumer<WiringHandler>()
            .Should()
            .Be("flight-wiring-handler");
    }

    [Fact]
    public void explicit_options_overload_should_reject_null_options()
    {
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));

        var act = () => services.AddCustomMassTransit(TestEnvironment(), options: null!, TestAssembly);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void unknown_transport_type_should_fail_at_registration()
    {
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));

        var act = () => services.AddCustomMassTransit(TestEnvironment(), (TransportType)42, TestAssembly);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("transportType");
    }

    [Fact]
    public void consumers_should_only_be_registered_from_the_explicit_assemblies()
    {
        var withAssembly = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));
        var withoutAssembly = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));

        withAssembly.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory, TestAssembly);
        withoutAssembly.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory);

        withAssembly.Should().Contain(d => d.ServiceType == typeof(WiringHandler));
        withoutAssembly.Should().NotContain(d => d.ServiceType == typeof(WiringHandler));
        withoutAssembly.Should().NotContain(d => typeof(IConsumer).IsAssignableFrom(d.ServiceType));
    }

    [Fact]
    public void duplicate_and_null_assemblies_should_be_tolerated()
    {
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));

        var act = () =>
            services.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory, TestAssembly, null!, TestAssembly);

        act.Should().NotThrow();
        services.Count(d => d.ServiceType == typeof(WiringHandler)).Should().Be(1);
    }

    [Fact]
    public async Task test_environment_should_use_the_test_harness_and_service_prefixed_receive_endpoints()
    {
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));
        services.AddCustomMassTransit(
            TestEnvironment(),
            new MessageBrokerOptions { TransportType = TransportType.InMemory, ServiceName = "passenger-it" },
            TestAssembly
        );

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new WiringMessage(Guid.NewGuid()));

            (await harness.Consumed.Any<WiringMessage>()).Should().BeTrue();
            var address = provider.GetRequiredService<ConsumedAddresses>().Addresses.Should().ContainSingle().Subject;
            address.AbsolutePath.Trim('/').Should().Be("passenger-it-wiring-handler");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task inbox_enabled_should_record_each_delivery_keyed_by_message_id_and_queue_before_the_consumer_runs()
    {
        var processor = EmptyInbox();
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));
        services.AddSingleton(processor);
        services.AddCustomMassTransit(
            TestEnvironment(),
            new MessageBrokerOptions
            {
                TransportType = TransportType.InMemory,
                ServiceName = "passenger-it",
                UseInbox = true,
            },
            TestAssembly
        );

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new WiringMessage(Guid.NewGuid()));

            (await harness.Consumed.Any<WiringMessage>()).Should().BeTrue();
            var consumed = harness.Consumed.Select<WiringMessage>().Single().Context;
            consumed.MessageId.Should().NotBeNull();
            var expectedInboxId = InboxMessageId.Create(
                consumed.MessageId!.Value,
                consumed.ReceiveContext.InputAddress
            );

            await processor
                .Received(1)
                .AddReceivedMessageAsync(expectedInboxId, Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
            await processor.Received(1).ProcessInboxAsync(expectedInboxId, Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task inbox_disabled_should_not_touch_the_persist_message_store()
    {
        var processor = EmptyInbox();
        var services = CreateServices(BuildConfiguration(new Dictionary<string, string?>()));
        services.AddSingleton(processor);
        services.AddCustomMassTransit(
            TestEnvironment(),
            new MessageBrokerOptions { TransportType = TransportType.InMemory, UseInbox = false },
            TestAssembly
        );

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new WiringMessage(Guid.NewGuid()));

            (await harness.Consumed.Any<WiringMessage>()).Should().BeTrue();
            await processor
                .DidNotReceiveWithAnyArgs()
                .AddReceivedMessageAsync(default(Guid), Arg.Any<MessageEnvelope>(), default);
            await processor.DidNotReceiveWithAnyArgs().ProcessInboxAsync(default, default);
        }
        finally
        {
            await harness.Stop();
        }
    }

    public record WiringMessage(Guid Id);

    public class ConsumedAddresses
    {
        public List<Uri> Addresses { get; } = new();
    }

    public class WiringHandler : IConsumer<WiringMessage>
    {
        private readonly ConsumedAddresses _addresses;

        public WiringHandler(ConsumedAddresses addresses)
        {
            _addresses = addresses;
        }

        public Task Consume(ConsumeContext<WiringMessage> context)
        {
            _addresses.Addresses.Add(context.ReceiveContext.InputAddress);
            return Task.CompletedTask;
        }
    }
}
