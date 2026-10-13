namespace Unit.Test.Messaging;

using System.Linq.Expressions;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public class ConsumeFilterTests
{
    private static readonly Uri InputAddress = new("rabbitmq://localhost/passenger-register-new-user-handler");

    private readonly IPersistMessageProcessor _processor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPipe<ConsumeContext<UserCreated>> _next = Substitute.For<IPipe<ConsumeContext<UserCreated>>>();

    private ConsumeFilter<UserCreated> CreateFilter() =>
        new(_processor, NullLogger<ConsumeFilter<UserCreated>>.Instance);

    private static ConsumeContext<UserCreated> CreateContext(Guid? messageId, Uri? inputAddress = null)
    {
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(new UserCreated(Guid.NewGuid()));
        context.MessageId.Returns(messageId);
        context.Headers.Returns(Substitute.For<Headers>());
        context.CancellationToken.Returns(CancellationToken.None);

        var receiveContext = Substitute.For<ReceiveContext>();
        receiveContext.InputAddress.Returns(inputAddress ?? InputAddress);
        context.ReceiveContext.Returns(receiveContext);

        return context;
    }

    private void SetupExisting(params PersistMessage[] messages)
    {
        _processor
            .GetByFilterAsync(Arg.Any<Expression<Func<PersistMessage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(messages.ToList().AsReadOnly());
    }

    [Fact]
    public async Task first_delivery_should_record_inbox_row_keyed_by_message_id_and_queue_then_run_consumer()
    {
        var messageId = Guid.NewGuid();
        var expectedInboxId = InboxMessageId.Create(messageId, InputAddress);
        var context = CreateContext(messageId);
        SetupExisting();

        await CreateFilter().Send(context, _next);

        Received.InOrder(() =>
        {
            _processor.AddReceivedMessageAsync(
                expectedInboxId,
                Arg.Any<MessageEnvelope>(),
                Arg.Any<CancellationToken>()
            );
            _next.Send(context);
            _processor.ProcessInboxAsync(expectedInboxId, Arg.Any<CancellationToken>());
        });

        await _processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task first_delivery_should_persist_the_message_and_its_headers_in_the_envelope()
    {
        var messageId = Guid.NewGuid();
        var context = CreateContext(messageId);
        var headers = Substitute.For<Headers>();
        headers.GetEnumerator().Returns(_ => new List<HeaderValue> { new("CorrelationId", "abc") }.GetEnumerator());
        context.Headers.Returns(headers);
        SetupExisting();

        await CreateFilter().Send(context, _next);

        await _processor
            .Received(1)
            .AddReceivedMessageAsync(
                Arg.Any<Guid>(),
                Arg.Is<MessageEnvelope>(e =>
                    ReferenceEquals(e.Message, context.Message)
                    && e.Headers.ContainsKey("CorrelationId")
                    && Equals(e.Headers["CorrelationId"], "abc")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task redelivery_of_an_already_processed_message_should_be_skipped_without_running_the_consumer()
    {
        var messageId = Guid.NewGuid();
        var inboxId = InboxMessageId.Create(messageId, InputAddress);
        var processed = new PersistMessage(inboxId, nameof(UserCreated), "{}", MessageDeliveryType.Inbox);
        processed.ChangeState(MessageStatus.Processed);
        SetupExisting(processed);

        await CreateFilter().Send(CreateContext(messageId), _next);

        await _next.DidNotReceive().Send(Arg.Any<ConsumeContext<UserCreated>>());
        await _processor
            .DidNotReceiveWithAnyArgs()
            .AddReceivedMessageAsync(default(Guid), Arg.Any<MessageEnvelope>(), default);
        await _processor.DidNotReceiveWithAnyArgs().ProcessInboxAsync(default, default);
    }

    [Fact]
    public async Task redelivery_of_an_in_progress_message_should_run_the_consumer_without_a_second_inbox_row()
    {
        var messageId = Guid.NewGuid();
        var inboxId = InboxMessageId.Create(messageId, InputAddress);
        SetupExisting(new PersistMessage(inboxId, nameof(UserCreated), "{}", MessageDeliveryType.Inbox));
        var context = CreateContext(messageId);

        await CreateFilter().Send(context, _next);

        await _processor
            .DidNotReceiveWithAnyArgs()
            .AddReceivedMessageAsync(default(Guid), Arg.Any<MessageEnvelope>(), default);
        await _next.Received(1).Send(context);
        await _processor.Received(1).ProcessInboxAsync(inboxId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task delivery_without_message_id_should_be_recorded_with_a_generated_id_and_marked_processed()
    {
        var generatedId = Guid.NewGuid();
        _processor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(generatedId);
        var context = CreateContext(messageId: null);

        await CreateFilter().Send(context, _next);

        Received.InOrder(() =>
        {
            _processor.AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
            _next.Send(context);
            _processor.ProcessInboxAsync(generatedId, Arg.Any<CancellationToken>());
        });

        await _processor.DidNotReceiveWithAnyArgs().GetByFilterAsync(default!, default);
        await _processor
            .DidNotReceiveWithAnyArgs()
            .AddReceivedMessageAsync(default(Guid), Arg.Any<MessageEnvelope>(), default);
    }

    [Fact]
    public async Task consumer_failure_should_leave_the_inbox_row_unprocessed_so_the_retry_can_run()
    {
        var messageId = Guid.NewGuid();
        var context = CreateContext(messageId);
        SetupExisting();
        _next.Send(context).Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = () => CreateFilter().Send(context, _next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _processor.DidNotReceiveWithAnyArgs().ProcessInboxAsync(default, default);
    }

    [Fact]
    public async Task same_message_on_different_queues_should_use_different_inbox_rows()
    {
        var messageId = Guid.NewGuid();
        var passengerQueue = new Uri("rabbitmq://localhost/passenger-register-new-user-handler");
        var bookingQueue = new Uri("rabbitmq://localhost/booking-register-new-user-handler");
        SetupExisting();

        await CreateFilter().Send(CreateContext(messageId, passengerQueue), _next);
        await CreateFilter().Send(CreateContext(messageId, bookingQueue), _next);

        await _processor
            .Received(1)
            .AddReceivedMessageAsync(
                InboxMessageId.Create(messageId, passengerQueue),
                Arg.Any<MessageEnvelope>(),
                Arg.Any<CancellationToken>()
            );
        await _processor
            .Received(1)
            .AddReceivedMessageAsync(
                InboxMessageId.Create(messageId, bookingQueue),
                Arg.Any<MessageEnvelope>(),
                Arg.Any<CancellationToken>()
            );
        await _next.Received(2).Send(Arg.Any<ConsumeContext<UserCreated>>());
    }

    public record UserCreated(Guid Id);
}
