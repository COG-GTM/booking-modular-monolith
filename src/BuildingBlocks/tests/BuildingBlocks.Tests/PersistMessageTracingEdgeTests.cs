using System.Diagnostics;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class PersistMessageTracingEdgeTests
{
    [Fact]
    public void missing_current_activity_leaves_envelope_unchanged()
    {
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            var envelope = new MessageEnvelope(new TraceEvent(Guid.NewGuid()));
            PersistMessageTracing.WithCurrentTraceContext(envelope).Should().BeSameAs(envelope);
            envelope.Headers.Should().BeEmpty();
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01")]
    public void invalid_or_missing_parent_returns_default_context(string? parent)
    {
        var headers = new Dictionary<string, object?>();
        if (parent is not null)
            headers[PersistMessageTracing.TraceParentHeader] = parent;

        PersistMessageTracing.GetParentContext(headers).Should().Be(default(ActivityContext));
    }

    [Fact]
    public void trace_state_is_copied_without_mutating_the_original_headers()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = "vendor=value";
        var envelope = new MessageEnvelope(
            new TraceEvent(Guid.NewGuid()),
            new Dictionary<string, object?> { ["tenant"] = "one" }
        );

        var traced = PersistMessageTracing.WithCurrentTraceContext(envelope, activity);

        traced
            .Headers.Should()
            .Contain("tenant", "one")
            .And.Contain("traceparent", activity.Id)
            .And.Contain("tracestate", "vendor=value");
        envelope.Headers.Should().ContainSingle();
        var parent = PersistMessageTracing.GetParentContext(traced.Headers);
        parent.IsRemote.Should().BeTrue();
        parent.TraceState.Should().Be("vendor=value");
    }

    [Theory]
    [InlineData("traceparent", "tracestate")]
    [InlineData("TraceParent", "TraceState")]
    public async Task outbox_preserves_custom_headers_but_does_not_copy_stored_trace_headers(
        string parentKey,
        string stateKey
    )
    {
        await using var store = new PersistMessageDbContext<PersistMessageTracingEdgeTests>(
            new DbContextOptionsBuilder<PersistMessageDbContext<PersistMessageTracingEdgeTests>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var endpoint = Substitute.For<IPublishEndpoint>();
        IPipe<PublishContext>? pipe = null;
        endpoint
            .Publish(
                Arg.Any<object>(),
                Arg.Do<IPipe<PublishContext>>(value => pipe = value),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var processor = new PersistMessageProcessor<PersistMessageTracingEdgeTests>(
            NullLogger<PersistMessageProcessor<PersistMessageTracingEdgeTests>>.Instance,
            Substitute.For<IMediator>(),
            store,
            endpoint
        );
        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TraceEvent(Guid.NewGuid()),
                new Dictionary<string, object?>
                {
                    [parentKey] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                    [stateKey] = "vendor=value",
                    ["tenant"] = "one",
                }
            )
        );
        var row = await store.PersistMessage.SingleAsync();

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        pipe.Should().NotBeNull();
        var context = Substitute.For<PublishContext>();
        var headers = Substitute.For<SendHeaders>();
        context.Headers.Returns(headers);
        await pipe!.Send(context);
        headers
            .Received(1)
            .Set("tenant", Arg.Is<object?>(value => value != null && value.ToString() == "one"), Arg.Any<bool>());
        headers
            .DidNotReceive()
            .Set(Arg.Is<string>(key => PersistMessageTracing.IsTraceHeader(key)), Arg.Any<object>(), Arg.Any<bool>());
        context.Received(1).MessageId = row.Id;
        row.MessageStatus.Should().Be(MessageStatus.Processed);
    }

    private sealed record TraceEvent(Guid EventId) : IIntegrationEvent;
}
