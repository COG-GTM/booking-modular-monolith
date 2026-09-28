using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Contract.Test.Messaging;

/// <summary>
/// The outbox loop that publishes to RabbitMQ runs on a background thread with no ambient trace. The dispatcher
/// stores the W3C trace context in the persisted envelope and the processor re-parents its publish span on it, so a
/// consumer in another service is linked back to the originating request.
/// </summary>
public class OutboxTraceContextPropagationTests : IDisposable
{
    private const string OutboxActivityName = "persist-message.outbox.publish";
    private readonly ActivitySource _requestSource = new("Contract.Test.Request");
    private readonly List<Activity> _startedActivities = new();
    private readonly ActivityListener _listener;

    public OutboxTraceContextPropagationTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == _requestSource.Name || source.Name == PersistMessageActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _startedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        Activity.Current = null;
        _listener.Dispose();
        _requestSource.Dispose();
    }

    [Fact]
    public async Task dispatcher_stores_current_trace_context_in_outbox_envelope_headers()
    {
        var persistMessageProcessor = Substitute.For<IPersistMessageProcessor>();
        var dispatcher = CreateDispatcher(persistMessageProcessor);

        using var request = _requestSource.StartActivity("incoming-request")!;
        request.TraceStateString = "vendor=abc";

        await dispatcher.SendAsync(new FlightCreated(Guid.NewGuid()));

        var envelope = CapturedEnvelope(persistMessageProcessor);
        envelope.Headers.Should().ContainKey(TraceContextHeaders.TraceParent).WhoseValue.Should().Be(request.Id);
        envelope.Headers.Should().ContainKey(TraceContextHeaders.TraceState).WhoseValue.Should().Be("vendor=abc");
        envelope.Headers.Keys.Should().Contain(["CorrelationId", "UserId", "UserName"]);
    }

    [Fact]
    public async Task dispatcher_omits_trace_headers_when_there_is_no_current_activity()
    {
        var persistMessageProcessor = Substitute.For<IPersistMessageProcessor>();
        var dispatcher = CreateDispatcher(persistMessageProcessor);
        Activity.Current = null;

        await dispatcher.SendAsync(new FlightCreated(Guid.NewGuid()));

        var envelope = CapturedEnvelope(persistMessageProcessor);
        envelope.Headers.Should().NotContainKey(TraceContextHeaders.TraceParent);
        envelope.Headers.Should().NotContainKey(TraceContextHeaders.TraceState);
    }

    [Fact]
    public void trace_context_header_names_follow_the_w3c_trace_context_specification()
    {
        TraceContextHeaders.TraceParent.Should().Be("traceparent");
        TraceContextHeaders.TraceState.Should().Be("tracestate");
    }

    [Fact]
    public async Task outbox_publish_is_parented_on_the_persisted_trace_context()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceParent = $"00-{traceId}-{spanId}-01";
        var headers = new Dictionary<string, object?>
        {
            [TraceContextHeaders.TraceParent] = traceParent,
            [TraceContextHeaders.TraceState] = "vendor=abc",
        };

        var (processor, publishEndpoint, dbContext) = await CreateProcessorWithOutboxMessage(headers);
        Activity? publishActivity = null;
        publishEndpoint
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                publishActivity = Activity.Current;
                return Task.CompletedTask;
            });

        await processor.ProcessAllAsync();

        publishActivity.Should().NotBeNull();
        publishActivity!.OperationName.Should().Be(OutboxActivityName);
        publishActivity.Source.Name.Should().Be(PersistMessageActivitySource.Name);
        publishActivity.Kind.Should().Be(ActivityKind.Producer);
        publishActivity.TraceId.Should().Be(traceId);
        publishActivity.ParentSpanId.Should().Be(spanId);
        publishActivity.TraceStateString.Should().Be("vendor=abc");
        publishActivity.Recorded.Should().BeTrue();

        Activity.Current.Should().BeNull("the outbox span must be disposed once the publish completes");
        (await dbContext.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task outbox_publish_forwards_trace_headers_to_the_transport_message()
    {
        var traceParent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
        var headers = new Dictionary<string, object?>
        {
            [TraceContextHeaders.TraceParent] = traceParent,
            ["CorrelationId"] = Guid.NewGuid().ToString(),
        };

        var (processor, publishEndpoint, _) = await CreateProcessorWithOutboxMessage(headers);
        var publishContext = Substitute.For<PublishContext>();
        var forwardedHeaders = Substitute.For<SendHeaders>();
        publishContext.Headers.Returns(forwardedHeaders);
        publishEndpoint
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IPipe<PublishContext>>().Send(publishContext));

        await processor.ProcessAllAsync();

        // Envelope headers round-trip through JSON, so they reach the transport as JsonElement values.
        forwardedHeaders
            .Received(1)
            .Set(TraceContextHeaders.TraceParent, Arg.Is<object>(v => v.ToString() == traceParent), Arg.Any<bool>());
        forwardedHeaders
            .Received(1)
            .Set(
                "CorrelationId",
                Arg.Is<object>(v => v.ToString() == headers["CorrelationId"]!.ToString()),
                Arg.Any<bool>()
            );
    }

    [Theory]
    [MemberData(nameof(HeadersWithoutUsableTraceContext))]
    public async Task outbox_publish_still_happens_without_a_trace_span_when_trace_context_is_missing_or_invalid(
        Dictionary<string, object?> headers
    )
    {
        var (processor, publishEndpoint, dbContext) = await CreateProcessorWithOutboxMessage(headers);
        Activity? publishActivity = null;
        publishEndpoint
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                publishActivity = Activity.Current;
                return Task.CompletedTask;
            });

        await processor.ProcessAllAsync();

        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<object>(m => m is FlightCreated),
                Arg.Any<IPipe<PublishContext>>(),
                Arg.Any<CancellationToken>()
            );
        publishActivity.Should().BeNull();
        _startedActivities.Should().NotContain(a => a.OperationName == OutboxActivityName);
        (await dbContext.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    public static TheoryData<Dictionary<string, object?>> HeadersWithoutUsableTraceContext =>
        new()
        {
            new Dictionary<string, object?>(),
            new Dictionary<string, object?> { [TraceContextHeaders.TraceParent] = null },
            new Dictionary<string, object?> { [TraceContextHeaders.TraceParent] = string.Empty },
            new Dictionary<string, object?> { [TraceContextHeaders.TraceParent] = "not-a-traceparent" },
            new Dictionary<string, object?> { [TraceContextHeaders.TraceState] = "vendor=abc" },
        };

    private static EventDispatcher CreateDispatcher(IPersistMessageProcessor persistMessageProcessor)
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        return new EventDispatcher(
            scopeFactory,
            Substitute.For<IEventMapper>(),
            NullLogger<EventDispatcher>.Instance,
            persistMessageProcessor,
            httpContextAccessor
        );
    }

    private static MessageEnvelope CapturedEnvelope(IPersistMessageProcessor persistMessageProcessor)
    {
        var call = persistMessageProcessor
            .ReceivedCalls()
            .Should()
            .ContainSingle(c => c.GetMethodInfo().Name == nameof(IPersistMessageProcessor.PublishMessageAsync))
            .Subject;

        return call.GetArguments().OfType<MessageEnvelope>().Single();
    }

    private static async Task<(
        PersistMessageProcessor Processor,
        IPublishEndpoint PublishEndpoint,
        PersistMessageDbContext DbContext
    )> CreateProcessorWithOutboxMessage(IDictionary<string, object?> headers)
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new PersistMessageDbContext(options);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var processor = new PersistMessageProcessor(
            NullLogger<PersistMessageProcessor>.Instance,
            Substitute.For<IMediator>(),
            dbContext,
            publishEndpoint
        );

        // Persist with no ambient activity so the only trace context in the envelope is the one under test.
        Activity.Current = null;
        await processor.PublishMessageAsync(new MessageEnvelope(new FlightCreated(Guid.NewGuid()), headers));

        return (processor, publishEndpoint, dbContext);
    }
}
