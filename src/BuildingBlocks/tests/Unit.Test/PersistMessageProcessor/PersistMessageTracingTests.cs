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

public class PersistMessageTracingTests
{
    private static readonly ActivitySource RequestActivitySource = new("PersistMessageTracingTests.Request");

    [Fact]
    public async Task saving_message_with_current_activity_persists_trace_context()
    {
        using var listener = CreateActivityListener("PersistMessageTracingTests.Request");
        var (processor, dbContext, _) = CreateProcessor();
        using var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server);

        await processor.PublishMessageAsync(new MessageEnvelope(new TestIntegrationEvent("value")));

        var message = await dbContext.PersistMessage.SingleAsync();
        using var data = JsonDocument.Parse(message.Data);
        data.RootElement.GetProperty("Headers")
            .GetProperty(PersistMessageTracing.TraceParentHeader)
            .GetString()
            .Should()
            .Be(requestActivity!.Id);
    }

    [Fact]
    public async Task processing_outbox_message_publishes_with_parent_activity_and_omits_trace_headers()
    {
        using var listener = CreateActivityListener(
            "PersistMessageTracingTests.Request",
            PersistMessageTracing.ActivitySourceName
        );
        var (processor, dbContext, publishEndpoint) = CreateProcessor();
        Activity? publishActivity = null;
        var publishContext = Substitute.For<PublishContext>();
        var headers = publishContext.Headers;
        var forwardedHeaders = new Dictionary<string, object?>();
        headers
            .When(x => x.Set(Arg.Any<string>(), Arg.Any<object>()))
            .Do(call => forwardedHeaders[call.Arg<string>()] = call.Arg<object>());
        publishEndpoint
            .When(endpoint =>
                endpoint.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            )
            .Do(call =>
            {
                publishActivity = Activity.Current;
                call.Arg<IPipe<PublishContext>>().Send(publishContext);
            });

        var requestActivity = RequestActivitySource.StartActivity("request", ActivityKind.Server);
        var traceId = requestActivity!.TraceId;
        var spanId = requestActivity.SpanId;
        await processor.PublishMessageAsync(new MessageEnvelope(new TestIntegrationEvent("value")));
        var messageId = (await dbContext.PersistMessage.SingleAsync()).Id;
        requestActivity.Dispose();

        await processor.ProcessAsync(messageId, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.DisplayName.Should().StartWith("outbox publish ");
        publishActivity.TraceId.Should().Be(traceId);
        publishActivity.ParentSpanId.Should().Be(spanId);
        forwardedHeaders.Should().NotContainKey(PersistMessageTracing.TraceParentHeader);
        forwardedHeaders.Should().NotContainKey(PersistMessageTracing.TraceStateHeader);
    }

    [Fact]
    public async Task processing_message_without_parent_activity_succeeds()
    {
        using var listener = CreateActivityListener(PersistMessageTracing.ActivitySourceName);
        var (processor, dbContext, publishEndpoint) = CreateProcessor();
        publishEndpoint
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        await processor.PublishMessageAsync(new MessageEnvelope(new TestIntegrationEvent("value")));
        var messageId = (await dbContext.PersistMessage.SingleAsync()).Id;

        var act = () => processor.ProcessAsync(messageId, MessageDeliveryType.Outbox);

        await act.Should().NotThrowAsync();
        await publishEndpoint
            .Received(1)
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
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
        IPublishEndpoint PublishEndpoint
    ) CreateProcessor()
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new PersistMessageDbContext<TestModule>(options);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var processor = new PersistMessageProcessor<TestModule>(
            NullLogger<PersistMessageProcessor<TestModule>>.Instance,
            Substitute.For<IMediator>(),
            dbContext,
            publishEndpoint
        );

        return (processor, dbContext, publishEndpoint);
    }

    public sealed class TestModule;

    public sealed record TestIntegrationEvent(string Value) : IEvent;
}
