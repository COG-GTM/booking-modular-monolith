using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.PersistMessageProcessor;

// Complements PersistMessageTracingTests: covers the internal-command path, tracestate, the
// "existing traceparent wins" rule, the no-activity and malformed-traceparent fallbacks and the
// forwarding of non-trace headers to MassTransit.
public class PersistMessageTraceContextTests
{
    private const string RequestSourceName = "PersistMessageTraceContextTests.Request";
    private static readonly ActivitySource RequestActivitySource = new(RequestSourceName);

    [Fact]
    public async Task saving_message_persists_tracestate_alongside_traceparent()
    {
        using var listener = CreateActivityListener(RequestSourceName);
        var (processor, dbContext, _, _) = CreateProcessor();
        using var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server)!;
        requestActivity.TraceStateString = "vendor=abc";

        await processor.PublishMessageAsync(new MessageEnvelope(new TestIntegrationEvent("value")));

        var headers = await ReadPersistedHeadersAsync(dbContext);
        headers.GetProperty(PersistMessageTracing.TraceParentHeader).GetString().Should().Be(requestActivity.Id);
        headers.GetProperty(PersistMessageTracing.TraceStateHeader).GetString().Should().Be("vendor=abc");
    }

    [Fact]
    public async Task saving_message_without_tracestate_does_not_persist_an_empty_tracestate_header()
    {
        using var listener = CreateActivityListener(RequestSourceName);
        var (processor, dbContext, _, _) = CreateProcessor();
        using var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server)!;

        await processor.PublishMessageAsync(new MessageEnvelope(new TestIntegrationEvent("value")));

        var headers = await ReadPersistedHeadersAsync(dbContext);
        headers.TryGetProperty(PersistMessageTracing.TraceStateHeader, out _).Should().BeFalse();
    }

    [Fact]
    public async Task saving_message_keeps_an_explicitly_supplied_traceparent()
    {
        using var listener = CreateActivityListener(RequestSourceName);
        var (processor, dbContext, _, _) = CreateProcessor();
        const string suppliedTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        using var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server)!;

        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TestIntegrationEvent("value"),
                new Dictionary<string, object?> { [PersistMessageTracing.TraceParentHeader] = suppliedTraceParent }
            )
        );

        var headers = await ReadPersistedHeadersAsync(dbContext);
        headers.GetProperty(PersistMessageTracing.TraceParentHeader).GetString().Should().Be(suppliedTraceParent);
        headers.GetProperty(PersistMessageTracing.TraceParentHeader).GetString().Should().NotBe(requestActivity.Id);
    }

    [Fact]
    public async Task saving_message_without_current_activity_adds_no_trace_headers()
    {
        Activity.Current = null;
        var (processor, dbContext, _, _) = CreateProcessor();

        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TestIntegrationEvent("value"),
                new Dictionary<string, object?> { ["x-custom"] = "kept" }
            )
        );

        var headers = await ReadPersistedHeadersAsync(dbContext);
        headers.TryGetProperty(PersistMessageTracing.TraceParentHeader, out _).Should().BeFalse();
        headers.TryGetProperty(PersistMessageTracing.TraceStateHeader, out _).Should().BeFalse();
        headers.GetProperty("x-custom").GetString().Should().Be("kept");
    }

    [Fact]
    public async Task processing_internal_command_runs_the_handler_under_a_span_parented_to_the_saved_trace()
    {
        using var listener = CreateActivityListener(RequestSourceName, PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, _, mediator) = CreateProcessor();
        Activity? handlerActivity = null;
        mediator
            .When(m => m.Send(Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(_ => handlerActivity = Activity.Current);

        var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server)!;
        requestActivity.TraceStateString = "vendor=abc";
        var traceId = requestActivity.TraceId;
        var spanId = requestActivity.SpanId;
        await processor.AddInternalMessageAsync(new TestInternalCommand("value"));
        var message = await dbContext.PersistMessage.SingleAsync();
        requestActivity.Dispose();
        Activity.Current = null;

        await processor.ProcessAsync(message.Id, MessageDeliveryType.Internal);

        await mediator.Received(1).Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
        handlerActivity.Should().NotBeNull();
        handlerActivity!.Source.Name.Should().Be(PersistMessageTracing.ActivitySourceName);
        handlerActivity.DisplayName.Should().Be($"internal command {nameof(TestInternalCommand)}");
        handlerActivity.TraceId.Should().Be(traceId);
        handlerActivity.ParentSpanId.Should().Be(spanId);
        handlerActivity.TraceStateString.Should().Be("vendor=abc");
        handlerActivity.GetTagItem("messaging.message.id").Should().Be(message.Id);
        handlerActivity.GetTagItem("outbox.delivery_type").Should().Be(nameof(MessageDeliveryType.Internal));
        handlerActivity.GetTagItem("messaging.message.type").Should().Be(message.DataType);
        (await dbContext.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task processing_outbox_message_tags_the_publish_span_with_message_metadata()
    {
        using var listener = CreateActivityListener(RequestSourceName, PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, publishEndpoint, _) = CreateProcessor();
        Activity? publishActivity = null;
        publishEndpoint
            .When(e => e.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>()))
            .Do(_ => publishActivity = Activity.Current);

        var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server)!;
        await processor.PublishMessageAsync(new MessageEnvelope(new TestIntegrationEvent("value")));
        var message = await dbContext.PersistMessage.SingleAsync();
        requestActivity.Dispose();
        Activity.Current = null;

        await processor.ProcessAsync(message.Id, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.Source.Name.Should().Be(PersistMessageTracing.ActivitySourceName);
        publishActivity.DisplayName.Should().Be($"outbox publish {nameof(TestIntegrationEvent)}");
        publishActivity.Kind.Should().Be(ActivityKind.Internal);
        publishActivity.GetTagItem("messaging.message.id").Should().Be(message.Id);
        publishActivity.GetTagItem("outbox.delivery_type").Should().Be(nameof(MessageDeliveryType.Outbox));
        publishActivity.GetTagItem("messaging.message.type").Should().Be(message.DataType);
    }

    [Fact]
    public async Task processing_outbox_message_with_malformed_traceparent_starts_a_new_root_trace_and_publishes()
    {
        using var listener = CreateActivityListener(PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, publishEndpoint, _) = CreateProcessor();
        Activity? publishActivity = null;
        publishEndpoint
            .When(e => e.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>()))
            .Do(_ => publishActivity = Activity.Current);
        Activity.Current = null;
        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TestIntegrationEvent("value"),
                new Dictionary<string, object?> { [PersistMessageTracing.TraceParentHeader] = "not-a-traceparent" }
            )
        );
        var messageId = (await dbContext.PersistMessage.SingleAsync()).Id;

        await processor.ProcessAsync(messageId, MessageDeliveryType.Outbox);

        await publishEndpoint
            .Received(1)
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
        publishActivity.Should().NotBeNull();
        publishActivity!.ParentId.Should().BeNull();
        publishActivity.ParentSpanId.Should().Be(default(ActivitySpanId));
        (await dbContext.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task processing_outbox_message_restores_traceparent_with_case_insensitive_header_key()
    {
        using var listener = CreateActivityListener(PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, publishEndpoint, _) = CreateProcessor();
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        const string traceParent = $"00-{traceId}-b7ad6b7169203331-01";
        Activity? publishActivity = null;
        publishEndpoint
            .When(e => e.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>()))
            .Do(_ => publishActivity = Activity.Current);
        Activity.Current = null;

        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TestIntegrationEvent("value"),
                new Dictionary<string, object?> { ["TraceParent"] = traceParent }
            )
        );
        var messageId = (await dbContext.PersistMessage.SingleAsync()).Id;

        await processor.ProcessAsync(messageId, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.TraceId.ToHexString().Should().Be(traceId);
    }

    [Fact]
    public async Task processing_outbox_message_restores_tracestate_case_insensitively_without_forwarding_trace_headers()
    {
        using var listener = CreateActivityListener(PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, publishEndpoint, _) = CreateProcessor();
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        const string traceParent = $"00-{traceId}-b7ad6b7169203331-01";
        Activity? publishActivity = null;
        var forwardedHeaders = new Dictionary<string, object?>();
        var publishContext = Substitute.For<PublishContext>();
        publishContext
            .Headers.When(h => h.Set(Arg.Any<string>(), Arg.Any<object>()))
            .Do(call => forwardedHeaders[call.Arg<string>()] = call.Arg<object>());
        publishEndpoint
            .When(e => e.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                publishActivity = Activity.Current;
                call.Arg<IPipe<PublishContext>>().Send(publishContext);
            });
        Activity.Current = null;

        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TestIntegrationEvent("value"),
                new Dictionary<string, object?>
                {
                    ["TRACEPARENT"] = traceParent,
                    ["TraceState"] = "vendor=abc",
                    ["x-correlation-id"] = "corr-1",
                }
            )
        );
        var messageId = (await dbContext.PersistMessage.SingleAsync()).Id;

        await processor.ProcessAsync(messageId, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.TraceId.ToHexString().Should().Be(traceId);
        publishActivity.ParentSpanId.ToHexString().Should().Be("b7ad6b7169203331");
        publishActivity.TraceStateString.Should().Be("vendor=abc");
        forwardedHeaders.Keys.Should().BeEquivalentTo("x-correlation-id");
    }

    [Fact]
    public async Task processing_outbox_message_forwards_non_trace_headers_and_the_published_event()
    {
        using var listener = CreateActivityListener(RequestSourceName, PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, publishEndpoint, _) = CreateProcessor();
        object? publishedMessage = null;
        var forwardedHeaders = new Dictionary<string, object?>();
        var publishContext = Substitute.For<PublishContext>();
        publishContext
            .Headers.When(h => h.Set(Arg.Any<string>(), Arg.Any<object>()))
            .Do(call => forwardedHeaders[call.Arg<string>()] = call.Arg<object>());
        publishEndpoint
            .When(e => e.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                publishedMessage = call.Arg<object>();
                call.Arg<IPipe<PublishContext>>().Send(publishContext);
            });

        var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server)!;
        requestActivity.TraceStateString = "vendor=abc";
        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TestIntegrationEvent("value"),
                new Dictionary<string, object?> { ["x-correlation-id"] = "corr-1", ["x-user-id"] = "user-1" }
            )
        );
        var messageId = (await dbContext.PersistMessage.SingleAsync()).Id;
        requestActivity.Dispose();
        Activity.Current = null;

        await processor.ProcessAsync(messageId, MessageDeliveryType.Outbox);

        publishedMessage.Should().BeOfType<TestIntegrationEvent>().Which.Value.Should().Be("value");
        forwardedHeaders.Keys.Should().BeEquivalentTo("x-correlation-id", "x-user-id");
        forwardedHeaders["x-correlation-id"]!.ToString().Should().Be("corr-1");
        forwardedHeaders["x-user-id"]!.ToString().Should().Be("user-1");
    }

    private static async Task<JsonElement> ReadPersistedHeadersAsync(PersistMessageDbContext<TestModule> dbContext)
    {
        var message = await dbContext.PersistMessage.SingleAsync();
        using var data = JsonDocument.Parse(message.Data);
        return data.RootElement.GetProperty("Headers").Clone();
    }

    private static ActivityListener CreateActivityListener(params string[] sourceNames)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => sourceNames.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static (
        PersistMessageProcessor<TestModule> Processor,
        PersistMessageDbContext<TestModule> DbContext,
        IPublishEndpoint PublishEndpoint,
        IMediator Mediator
    ) CreateProcessor()
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new PersistMessageDbContext<TestModule>(options);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var mediator = Substitute.For<IMediator>();
        var processor = new PersistMessageProcessor<TestModule>(
            NullLogger<PersistMessageProcessor<TestModule>>.Instance,
            mediator,
            dbContext,
            publishEndpoint
        );

        return (processor, dbContext, publishEndpoint, mediator);
    }

    public sealed class TestModule;

    public sealed record TestIntegrationEvent(string Value) : IEvent;

    public sealed record TestInternalCommand(string Value) : IInternalCommand;
}
