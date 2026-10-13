namespace Unit.Test.Messaging;

using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public record TracedSeatReserved(Guid Id, string Seat) : IEvent;

public class PersistMessageProcessorTraceContextTests : IAsyncLifetime
{
    private const string RequestSourceName = "Unit.Test.Messaging.Request";

    private readonly PersistMessageDbContext _dbContext = new(
        new DbContextOptionsBuilder<PersistMessageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
    );

    private readonly ActivitySource _requestSource = new(RequestSourceName);
    private readonly List<Activity> _stopped = [];
    private readonly ActivityListener _listener;

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public PersistMessageProcessorTraceContextTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s =>
                s.Name
                    is RequestSourceName
                        or PersistMessageProcessor.ActivitySourceName
                        or MassTransit.Logging.DiagnosticHeaders.DefaultListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                lock (_stopped)
                    _stopped.Add(a);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransitTestHarness();
        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        _harness.TestInactivityTimeout = TimeSpan.FromSeconds(2);
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        _listener.Dispose();
        _requestSource.Dispose();
        await _harness.Stop();
        await _provider.DisposeAsync();
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.DisposeAsync();
    }

    private PersistMessageProcessor CreateProcessor(IPublishEndpoint? publishEndpoint = null) =>
        new(
            NullLogger<PersistMessageProcessor>.Instance,
            Substitute.For<IMediator>(),
            _dbContext,
            publishEndpoint ?? _harness.Bus
        );

    private async Task<PersistMessage> SingleRowAsync(MessageDeliveryType deliveryType) =>
        await _dbContext.PersistMessage.SingleAsync(x => x.DeliveryType == deliveryType);

    private static IDictionary<string, object?> StoredHeaders(PersistMessage row) =>
        JsonSerializer.Deserialize<MessageEnvelope>(row.Data)!.Headers;

    private List<Activity> Stopped(string sourceName)
    {
        lock (_stopped)
            return _stopped.Where(a => a.Source.Name == sourceName).ToList();
    }

    [Fact]
    public async Task publishing_an_outbox_message_inside_a_request_stores_the_request_trace_context_in_the_row()
    {
        string requestId;

        using (var request = _requestSource.StartActivity("POST /api/v1.0/flight"))
        {
            request!.TraceStateString = "vendor=abc";
            requestId = request.Id!;
            await CreateProcessor()
                .PublishMessageAsync(new MessageEnvelope(new TracedSeatReserved(Guid.NewGuid(), "12A")));
        }

        var headers = StoredHeaders(await SingleRowAsync(MessageDeliveryType.Outbox));

        headers["traceparent"]!.ToString().Should().Be(requestId);
        headers["tracestate"]!.ToString().Should().Be("vendor=abc");
    }

    [Fact]
    public async Task publishing_an_outbox_message_without_an_active_trace_stores_no_trace_headers()
    {
        Activity.Current.Should().BeNull();

        await CreateProcessor()
            .PublishMessageAsync(
                new MessageEnvelope(
                    new TracedSeatReserved(Guid.NewGuid(), "12A"),
                    new Dictionary<string, object?> { ["CorrelationId"] = "corr-1" }
                )
            );

        var headers = StoredHeaders(await SingleRowAsync(MessageDeliveryType.Outbox));

        headers.Keys.Should().BeEquivalentTo("CorrelationId");
    }

    [Fact]
    public async Task tracestate_header_is_only_stored_when_the_request_has_one()
    {
        using (_requestSource.StartActivity("POST /api/v1.0/flight"))
            await CreateProcessor()
                .PublishMessageAsync(new MessageEnvelope(new TracedSeatReserved(Guid.NewGuid(), "12A")));

        var headers = StoredHeaders(await SingleRowAsync(MessageDeliveryType.Outbox));

        headers.Should().ContainKey("traceparent");
        headers.Should().NotContainKey("tracestate");
    }

    [Fact]
    public async Task inbox_and_internal_rows_do_not_capture_the_current_trace()
    {
        var processor = CreateProcessor();

        using (_requestSource.StartActivity("consume"))
        {
            await processor.AddReceivedMessageAsync(new MessageEnvelope(new TracedSeatReserved(Guid.NewGuid(), "12A")));
        }

        StoredHeaders(await SingleRowAsync(MessageDeliveryType.Inbox)).Should().NotContainKey("traceparent");
    }

    [Fact]
    public async Task processing_the_outbox_row_publishes_inside_the_request_trace_and_does_not_forward_trace_headers()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var processor = CreateProcessor(publishEndpoint);
        Activity? duringPublish = null;
        publishEndpoint
            .When(x => x.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>()))
            .Do(_ => duringPublish = Activity.Current);

        ActivityTraceId traceId;
        ActivitySpanId requestSpanId;
        using (var request = _requestSource.StartActivity("POST /api/v1.0/flight"))
        {
            traceId = request!.TraceId;
            requestSpanId = request.SpanId;
            await processor.PublishMessageAsync(
                new MessageEnvelope(
                    new TracedSeatReserved(Guid.NewGuid(), "12A"),
                    new Dictionary<string, object?> { ["CorrelationId"] = "corr-1" }
                )
            );
        }
        var row = await SingleRowAsync(MessageDeliveryType.Outbox);

        // the background worker runs later, outside the request
        Activity.Current.Should().BeNull();
        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        duringPublish.Should().NotBeNull();
        duringPublish!.Source.Name.Should().Be(PersistMessageProcessor.ActivitySourceName);
        duringPublish.OperationName.Should().Be($"outbox publish {typeof(TracedSeatReserved)}");
        duringPublish.TraceId.Should().Be(traceId);
        duringPublish.ParentSpanId.Should().Be(requestSpanId);
        duringPublish.GetTagItem("messaging.message.id").Should().Be(row.Id.ToString());
        Stopped(PersistMessageProcessor.ActivitySourceName)
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeSameAs(duringPublish);

        var pipe = publishEndpoint
            .ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish))
            .GetArguments()
            .OfType<IPipe<PublishContext>>()
            .Single();
        var context = Substitute.For<PublishContext>();
        var headers = Substitute.For<SendHeaders>();
        context.Headers.Returns(headers);

        await pipe.Send(context);

        var setKeys = headers
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(SendHeaders.Set))
            .Select(c => (string)c.GetArguments()[0]!)
            .ToList();
        setKeys.Should().BeEquivalentTo("CorrelationId");
    }

    [Fact]
    public async Task trace_headers_are_stripped_case_insensitively_when_forwarding_outbox_headers()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var processor = CreateProcessor(publishEndpoint);
        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TracedSeatReserved(Guid.NewGuid(), "12A"),
                new Dictionary<string, object?>
                {
                    ["TraceParent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                    ["TRACESTATE"] = "vendor=abc",
                    ["CorrelationId"] = "corr-1",
                }
            )
        );
        var row = await SingleRowAsync(MessageDeliveryType.Outbox);

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        var pipe = publishEndpoint
            .ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish))
            .GetArguments()
            .OfType<IPipe<PublishContext>>()
            .Single();
        var context = Substitute.For<PublishContext>();
        var headers = Substitute.For<SendHeaders>();
        context.Headers.Returns(headers);

        await pipe.Send(context);

        headers
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(SendHeaders.Set))
            .Select(c => (string)c.GetArguments()[0]!)
            .Should()
            .BeEquivalentTo("CorrelationId");
    }

    [Fact]
    public async Task bus_send_span_joins_the_trace_of_the_request_that_wrote_the_outbox_row()
    {
        var processor = CreateProcessor();
        ActivityTraceId traceId;
        using (var request = _requestSource.StartActivity("POST /api/v1.0/flight"))
        {
            traceId = request!.TraceId;
            await processor.PublishMessageAsync(new MessageEnvelope(new TracedSeatReserved(Guid.NewGuid(), "12A")));
        }
        var row = await SingleRowAsync(MessageDeliveryType.Outbox);

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        (await _harness.Published.Any<TracedSeatReserved>()).Should().BeTrue();
        var outboxSpan = Stopped(PersistMessageProcessor.ActivitySourceName).Should().ContainSingle().Subject;
        var busSpans = Stopped(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName);
        busSpans.Should().NotBeEmpty();
        busSpans.Should().Contain(a => a.ParentSpanId == outboxSpan.SpanId);
        busSpans.Should().OnlyContain(a => a.TraceId == traceId);
    }
}
