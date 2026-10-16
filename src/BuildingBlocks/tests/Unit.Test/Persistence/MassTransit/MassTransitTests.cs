using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using MtExtensions = BuildingBlocks.MassTransit.Extensions;

namespace BuildingBlocks.Unit.Test.Persistence;

public class InboxMessageIdTests
{
    [Fact]
    public void create_is_deterministic()
    {
        var messageId = Guid.NewGuid();
        var address = new Uri("rabbitmq://host/exchange/persist-test");

        InboxMessageId.Create(messageId, address).Should().Be(InboxMessageId.Create(messageId, address));
    }

    [Fact]
    public void create_differs_per_endpoint_and_per_message_id()
    {
        var messageId = Guid.NewGuid();
        var a = new Uri("rabbitmq://host/exchange/queue-a");
        var b = new Uri("rabbitmq://host/exchange/queue-b");

        InboxMessageId.Create(messageId, a).Should().NotBe(InboxMessageId.Create(messageId, b));
        InboxMessageId.Create(messageId, a).Should().NotBe(InboxMessageId.Create(Guid.NewGuid(), a));
    }

    [Fact]
    public void create_ignores_leading_and_trailing_slashes()
    {
        var messageId = Guid.NewGuid();
        var plain = InboxMessageId.Create(messageId, new Uri("rabbitmq://host/queue"));
        var slashed = InboxMessageId.Create(messageId, new Uri("rabbitmq://host//queue/"));

        plain.Should().Be(slashed);
    }

    [Fact]
    public void create_treats_null_address_like_empty_path()
    {
        var messageId = Guid.NewGuid();

        InboxMessageId
            .Create(messageId, null)
            .Should()
            .Be(InboxMessageId.Create(messageId, new Uri("rabbitmq://host")));
    }
}

public class EndpointNameFormatterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void blank_service_name_returns_default_formatter(string? serviceName)
    {
        var formatter = MtExtensions.CreateEndpointNameFormatter(
            new MessageBrokerOptions { ServiceName = serviceName }
        );

        formatter.Should().BeSameAs(KebabCaseEndpointNameFormatter.Instance);
    }

    [Fact]
    public void service_name_prefixes_consumer_endpoint_without_namespace()
    {
        var formatter = MtExtensions.CreateEndpointNameFormatter(
            new MessageBrokerOptions { ServiceName = " flight- " }
        );

        formatter.Consumer<PersistTestConsumer>().Should().Be("flight-persist-test");
    }
}

public class InboxStoreResolverTests
{
    private static InboxStoreResolver CreateResolver() =>
        new(
            [typeof(InboxStoreResolverTests).Assembly],
            KebabCaseEndpointNameFormatter.Instance,
            [new PersistMessageStore("Flight", typeof(PersistMessageDbContext<PersistFlightModule>))]
        );

    [Fact]
    public void resolve_returns_module_processor_for_matching_endpoint()
    {
        var resolver = CreateResolver();
        var moduleProcessor = Substitute.For<IPersistMessageProcessor<PersistFlightModule>>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IPersistMessageProcessor<PersistFlightModule>)).Returns(moduleProcessor);

        // Endpoint name = kebab-case consumer name ("PersistTestConsumer" -> "persist-test").
        var resolved = resolver.Resolve(provider, new Uri("rabbitmq://host/vhost/PERSIST-TEST"));

        resolved.Should().BeSameAs(moduleProcessor);
    }

    [Fact]
    public void resolve_uses_last_path_segment_as_endpoint_name()
    {
        var resolver = CreateResolver();
        var moduleProcessor = Substitute.For<IPersistMessageProcessor<PersistFlightModule>>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IPersistMessageProcessor<PersistFlightModule>)).Returns(moduleProcessor);

        var resolved = resolver.Resolve(provider, new Uri("rabbitmq://host/v/nested/persist-test"));

        resolved.Should().BeSameAs(moduleProcessor);
    }

    [Fact]
    public void resolve_falls_back_to_unqualified_processor_for_unknown_endpoint()
    {
        var resolver = CreateResolver();
        var fallback = Substitute.For<IPersistMessageProcessor>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IPersistMessageProcessor)).Returns(fallback);

        resolver.Resolve(provider, new Uri("rabbitmq://host/unknown-queue")).Should().BeSameAs(fallback);
        resolver.Resolve(provider, null).Should().BeSameAs(fallback);
    }
}

public class ConsumeFilterTests
{
    private static (
        ConsumeContext<PersistPing> Context,
        ReceiveContext ReceiveContext,
        IPipe<ConsumeContext<PersistPing>> Next
    ) CreateContext(Guid? messageId, string inputAddress)
    {
        var context = Substitute.For<ConsumeContext<PersistPing>>();
        var receiveContext = Substitute.For<ReceiveContext>();
        receiveContext.InputAddress.Returns(new Uri(inputAddress));
        context.ReceiveContext.Returns(receiveContext);
        context.Message.Returns(new PersistPing(Guid.NewGuid()));
        context.MessageId.Returns(messageId);
        context.CancellationToken.Returns(CancellationToken.None);
        var headers = Substitute.For<Headers>();
        var headerValues = new List<HeaderValue> { new("tenant", "acme") };
        headers.GetEnumerator().Returns(_ => headerValues.GetEnumerator());
        context.Headers.Returns(headers);
        var next = Substitute.For<IPipe<ConsumeContext<PersistPing>>>();
        return (context, receiveContext, next);
    }

    private static ConsumeFilter<PersistPing> FilterWith(IPersistMessageProcessor processor) =>
        new(processor, NullLogger<ConsumeFilter<PersistPing>>.Instance);

    [Fact]
    public async Task no_processor_calls_next_only()
    {
        var (context, _, next) = CreateContext(Guid.NewGuid(), "rabbitmq://h/q");
        var resolver = new InboxStoreResolver(
            [typeof(ConsumeFilterTests).Assembly],
            KebabCaseEndpointNameFormatter.Instance,
            []
        );
        var provider = Substitute.For<IServiceProvider>();
        var filter = new ConsumeFilter<PersistPing>(
            provider,
            resolver,
            NullLogger<ConsumeFilter<PersistPing>>.Instance
        );

        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task no_message_id_records_then_processes_around_next()
    {
        var generatedId = Guid.NewGuid();
        var processor = Substitute.For<IPersistMessageProcessor>();
        processor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(generatedId);
        var (context, _, next) = CreateContext(null, "rabbitmq://h/q");
        var filter = FilterWith(processor);

        await filter.Send(context, next);

        Received.InOrder(() =>
        {
            processor.AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
            next.Send(context);
            processor.ProcessInboxAsync(generatedId, Arg.Any<CancellationToken>());
        });
        processor.Received(1).AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task new_message_uses_scoped_inbox_id_and_processes_it()
    {
        var messageId = Guid.NewGuid();
        var inputAddress = "rabbitmq://h/q";
        var expectedInboxId = InboxMessageId.Create(messageId, new Uri(inputAddress));
        var processor = Substitute.For<IPersistMessageProcessor>();
        processor
            .GetByFilterAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PersistMessage, bool>>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new List<PersistMessage>());
        var (context, _, next) = CreateContext(messageId, inputAddress);
        var filter = FilterWith(processor);

        await filter.Send(context, next);

        Received.InOrder(() =>
        {
            processor.AddReceivedMessageAsync(
                expectedInboxId,
                Arg.Any<MessageEnvelope>(),
                Arg.Any<CancellationToken>()
            );
            next.Send(context);
            processor.ProcessInboxAsync(expectedInboxId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task already_processed_message_skips_next()
    {
        var messageId = Guid.NewGuid();
        var inputAddress = "rabbitmq://h/q";
        var inboxId = InboxMessageId.Create(messageId, new Uri(inputAddress));
        var existing = new PersistMessage(inboxId, "type", "{}", MessageDeliveryType.Inbox);
        existing.ChangeState(MessageStatus.Processed);
        var processor = Substitute.For<IPersistMessageProcessor>();
        processor
            .GetByFilterAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PersistMessage, bool>>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new List<PersistMessage> { existing });
        var (context, _, next) = CreateContext(messageId, inputAddress);
        var filter = FilterWith(processor);

        await filter.Send(context, next);

        await next.DidNotReceive().Send(context);
        await processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<Guid>(), Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task in_progress_message_is_not_re_added_but_processed_after_next()
    {
        var messageId = Guid.NewGuid();
        var inputAddress = "rabbitmq://h/q";
        var inboxId = InboxMessageId.Create(messageId, new Uri(inputAddress));
        var existing = new PersistMessage(inboxId, "type", "{}", MessageDeliveryType.Inbox);
        var processor = Substitute.For<IPersistMessageProcessor>();
        processor
            .GetByFilterAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PersistMessage, bool>>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new List<PersistMessage> { existing });
        var (context, _, next) = CreateContext(messageId, inputAddress);
        var filter = FilterWith(processor);

        await filter.Send(context, next);

        await processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<Guid>(), Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            next.Send(context);
            processor.ProcessInboxAsync(inboxId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task resolver_constructor_routes_to_module_processor()
    {
        var resolver = new InboxStoreResolver(
            [typeof(ConsumeFilterTests).Assembly],
            KebabCaseEndpointNameFormatter.Instance,
            [new PersistMessageStore("Flight", typeof(PersistMessageDbContext<PersistFlightModule>))]
        );
        var moduleProcessor = Substitute.For<IPersistMessageProcessor<PersistFlightModule>>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IPersistMessageProcessor<PersistFlightModule>)).Returns(moduleProcessor);
        moduleProcessor
            .GetByFilterAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PersistMessage, bool>>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new List<PersistMessage>());
        var (context, _, next) = CreateContext(Guid.NewGuid(), "rabbitmq://h/persist-test");
        var filter = new ConsumeFilter<PersistPing>(
            provider,
            resolver,
            NullLogger<ConsumeFilter<PersistPing>>.Instance
        );

        await filter.Send(context, next);

        await moduleProcessor
            .Received()
            .GetByFilterAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<PersistMessage, bool>>>(),
                Arg.Any<CancellationToken>()
            );
        await next.Received(1).Send(context);
    }
}

public class AddCustomMassTransitTests
{
    private static IWebHostEnvironment TestEnvironment()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("test");
        return env;
    }

    [Fact]
    public void explicit_transport_registers_message_broker_options_singleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MessageBrokerOptions>().TransportType.Should().Be(TransportType.InMemory);
    }

    [Fact]
    public void configuration_overload_reads_transport_and_service_name()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                [new("MessageBroker:TransportType", "InMemory"), new("MessageBroker:ServiceName", "flight")]
            )
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddCustomMassTransit(configuration, TestEnvironment());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessageBrokerOptions>();
        options.TransportType.Should().Be(TransportType.InMemory);
        options.ServiceName.Should().Be("flight");
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();
        formatter.Consumer<PersistTestConsumer>().Should().Be("flight-persist-test");
    }

    [Fact]
    public async Task registers_inbox_store_resolver_and_test_harness_in_test_environment()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddCustomMassTransit(TestEnvironment(), TransportType.InMemory);

        await using var provider = services.BuildServiceProvider();
        provider.GetService<InboxStoreResolver>().Should().NotBeNull();
        provider.GetService<global::MassTransit.Testing.ITestHarness>().Should().NotBeNull();
    }
}
