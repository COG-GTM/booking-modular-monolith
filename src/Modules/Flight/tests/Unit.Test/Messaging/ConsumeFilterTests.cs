using System.Linq.Expressions;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Unit.Test.Messaging;

public class ConsumeFilterTests
{
    public record ProbeMessage(string Value);

    private static readonly IReadOnlyList<PersistMessage> NoRows = new List<PersistMessage>().AsReadOnly();

    private readonly IPersistMessageProcessor _processor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPipe<ConsumeContext<ProbeMessage>> _next = Substitute.For<IPipe<ConsumeContext<ProbeMessage>>>();
    private readonly ConsumeFilter<ProbeMessage> _filter;

    public ConsumeFilterTests()
    {
        _filter = new ConsumeFilter<ProbeMessage>(_processor);
    }

    [Fact]
    public async Task first_delivery_should_persist_the_inbox_row_under_the_transport_message_id_and_run_the_consumer()
    {
        var messageId = NewId.NextGuid();
        var context = CreateContext(messageId);
        var message = context.Message;
        SetupExistingRows(NoRows);

        await _filter.Send(context, _next);

        Received.InOrder(() =>
        {
            _processor.AddReceivedMessageAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, message)),
                messageId,
                Arg.Any<CancellationToken>()
            );
            _next.Send(context);
            _processor.ProcessInboxAsync(messageId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task redelivery_of_an_already_processed_message_should_skip_the_consumer()
    {
        var messageId = NewId.NextGuid();
        var context = CreateContext(messageId);
        SetupExistingRows(new[] { InboxRow(messageId, MessageStatus.Processed) });

        await _filter.Send(context, _next);

        await _next.DidNotReceive().Send(Arg.Any<ConsumeContext<ProbeMessage>>());
        await _processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _processor.DidNotReceive().ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task redelivery_of_an_in_progress_message_should_rerun_the_consumer_without_duplicating_the_inbox_row()
    {
        var messageId = NewId.NextGuid();
        var context = CreateContext(messageId);
        SetupExistingRows(new[] { InboxRow(messageId, MessageStatus.InProgress) });

        await _filter.Send(context, _next);

        await _processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _next.Received(1).Send(context);
        await _processor.Received(1).ProcessInboxAsync(messageId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task delivery_without_a_transport_message_id_should_use_one_generated_id_for_the_whole_pipeline()
    {
        var context = CreateContext(messageId: null);
        SetupExistingRows(NoRows);
        Guid? persistedId = null;
        await _processor.AddReceivedMessageAsync(
            Arg.Any<MessageEnvelope>(),
            Arg.Do<Guid?>(id => persistedId = id),
            Arg.Any<CancellationToken>()
        );

        await _filter.Send(context, _next);

        persistedId.Should().NotBeNull();
        persistedId.Should().NotBe(Guid.Empty);
        await _next.Received(1).Send(context);
        await _processor.Received(1).ProcessInboxAsync(persistedId!.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task lookup_should_match_only_the_inbox_row_with_the_transport_message_id()
    {
        var messageId = NewId.NextGuid();
        var context = CreateContext(messageId);
        Expression<Func<PersistMessage, bool>>? predicate = null;
        _processor
            .GetByFilterAsync(
                Arg.Do<Expression<Func<PersistMessage, bool>>>(p => predicate = p),
                Arg.Any<CancellationToken>()
            )
            .Returns(NoRows);

        await _filter.Send(context, _next);

        var matches = predicate!.Compile();
        matches(InboxRow(messageId, MessageStatus.InProgress)).Should().BeTrue();
        matches(InboxRow(messageId, MessageStatus.Processed)).Should().BeTrue();
        matches(new PersistMessage(messageId, "t", "{}", MessageDeliveryType.Outbox)).Should().BeFalse();
        matches(InboxRow(NewId.NextGuid(), MessageStatus.Processed)).Should().BeFalse();
    }

    [Fact]
    public async Task consumer_failure_should_propagate_and_leave_the_inbox_row_unprocessed()
    {
        var messageId = NewId.NextGuid();
        var context = CreateContext(messageId);
        SetupExistingRows(NoRows);
        _next.Send(context).ThrowsAsync(new InvalidOperationException("consumer failed"));

        var act = () => _filter.Send(context, _next);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("consumer failed");
        await _processor.DidNotReceive().ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private void SetupExistingRows(IReadOnlyList<PersistMessage> rows)
    {
        _processor
            .GetByFilterAsync(Arg.Any<Expression<Func<PersistMessage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(rows);
    }

    private static PersistMessage InboxRow(Guid id, MessageStatus status)
    {
        var row = new PersistMessage(id, typeof(ProbeMessage).ToString(), "{}", MessageDeliveryType.Inbox);
        row.ChangeState(status);
        return row;
    }

    private static ConsumeContext<ProbeMessage> CreateContext(Guid? messageId)
    {
        var context = Substitute.For<ConsumeContext<ProbeMessage>>();
        context.MessageId.Returns(messageId);
        context.Message.Returns(new ProbeMessage("hello"));
        context.Headers.Returns(Substitute.For<Headers>());
        return context;
    }
}
