using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.MassTransit;

public class ConsumeFilterTests
{
    private readonly IPersistMessageProcessor _persistMessageProcessor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPipe<ConsumeContext<FakeIntegrationEvent>> _next = Substitute.For<
        IPipe<ConsumeContext<FakeIntegrationEvent>>
    >();
    private readonly ConsumeContext<FakeIntegrationEvent> _context;
    private readonly FakeIntegrationEvent _message = new(Guid.NewGuid(), "consumed");
    private readonly ConsumeFilter<FakeIntegrationEvent> _sut;

    public ConsumeFilterTests()
    {
        _context = Substitute.For<ConsumeContext<FakeIntegrationEvent>>();
        _context.Message.Returns(_message);
        var headers = Substitute.For<Headers>();
        headers.GetEnumerator().Returns(_ => new List<HeaderValue> { new("UserId", "42") }.GetEnumerator());
        _context.Headers.Returns(headers);
        _sut = new ConsumeFilter<FakeIntegrationEvent>(_persistMessageProcessor);
    }

    [Fact]
    public async Task first_delivery_should_store_inbox_message_invoke_consumer_then_mark_inbox_processed()
    {
        _persistMessageProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(_message.EventId);
        _persistMessageProcessor
            .ExistMessageAsync(_message.EventId, Arg.Any<CancellationToken>())
            .Returns((PersistMessage?)null);

        await _sut.Send(_context, _next);

        Received.InOrder(() =>
        {
            _persistMessageProcessor.AddReceivedMessageAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, _message) && e.Headers.ContainsKey("UserId")),
                Arg.Any<CancellationToken>()
            );
            _persistMessageProcessor.ExistMessageAsync(_message.EventId, Arg.Any<CancellationToken>());
            _next.Send(_context);
            _persistMessageProcessor.ProcessInboxAsync(_message.EventId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task duplicate_delivery_of_processed_message_should_not_invoke_consumer_again()
    {
        _persistMessageProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(_message.EventId);
        _persistMessageProcessor
            .ExistMessageAsync(_message.EventId, Arg.Any<CancellationToken>())
            .Returns(
                new PersistMessage(
                    _message.EventId,
                    typeof(FakeIntegrationEvent).FullName!,
                    "{}",
                    MessageDeliveryType.Inbox
                )
            );

        await _sut.Send(_context, _next);

        await _next.DidNotReceiveWithAnyArgs().Send(Arg.Any<ConsumeContext<FakeIntegrationEvent>>());
        await _persistMessageProcessor
            .DidNotReceiveWithAnyArgs()
            .ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task consumer_failure_should_propagate_and_leave_inbox_message_unprocessed()
    {
        _persistMessageProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(_message.EventId);
        _persistMessageProcessor
            .ExistMessageAsync(_message.EventId, Arg.Any<CancellationToken>())
            .Returns((PersistMessage?)null);
        _next.Send(_context).Returns(Task.FromException(new InvalidOperationException("consumer failed")));

        var act = () => _sut.Send(_context, _next);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("consumer failed");
        await _persistMessageProcessor
            .DidNotReceiveWithAnyArgs()
            .ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
