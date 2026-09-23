using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Outbox;

public class PersistMessageProcessorTests : IDisposable
{
    private readonly PersistMessageDbContext _dbContext;
    private readonly IMediator _mediator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly PersistMessageProcessor _sut;

    public PersistMessageProcessorTests()
    {
        _dbContext = DbContextFactory.CreatePersistMessageDbContext();
        _mediator = Substitute.For<IMediator>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _sut = new PersistMessageProcessor(
            Substitute.For<ILogger<PersistMessageProcessor>>(),
            _mediator,
            _dbContext,
            _publishEndpoint
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task publish_message_should_save_outbox_message_with_event_id_and_in_progress_status()
    {
        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "outbox");

        await _sut.PublishMessageAsync(
            new MessageEnvelope(@event, new Dictionary<string, object?> { ["UserId"] = "7" })
        );

        var stored = await _dbContext.PersistMessage.SingleAsync();
        stored.Id.Should().Be(@event.EventId);
        stored.DeliveryType.Should().Be(MessageDeliveryType.Outbox);
        stored.MessageStatus.Should().Be(MessageStatus.InProgress);
        stored.DataType.Should().Be(typeof(FakeIntegrationEvent).FullName);
        stored.RetryCount.Should().Be(0);

        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(stored.Data);
        envelope!.Headers.Should().ContainKey("UserId");
        envelope.Message!.ToString().Should().Contain("outbox");
    }

    [Fact]
    public async Task add_received_message_should_save_inbox_message_and_return_event_id()
    {
        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "inbox");

        var id = await _sut.AddReceivedMessageAsync(new MessageEnvelope(@event));

        id.Should().Be(@event.EventId);
        var stored = await _dbContext.PersistMessage.SingleAsync();
        stored.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
        stored.MessageStatus.Should().Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task add_received_message_with_non_event_payload_should_generate_new_id()
    {
        var id = await _sut.AddReceivedMessageAsync(new MessageEnvelope(new FakeNotAnEvent("plain")));

        id.Should().NotBeEmpty();
        var stored = await _dbContext.PersistMessage.SingleAsync();
        stored.Id.Should().Be(id);
        stored.DataType.Should().Be(typeof(FakeNotAnEvent).FullName);
    }

    [Fact]
    public async Task add_internal_message_should_save_internal_message()
    {
        var command = new FakeInternalCommand(Guid.NewGuid(), "internal");

        await _sut.AddInternalMessageAsync(command);

        var stored = await _dbContext.PersistMessage.SingleAsync();
        stored.Id.Should().Be(command.EventId);
        stored.DeliveryType.Should().Be(MessageDeliveryType.Internal);
        stored.DataType.Should().Be(typeof(FakeInternalCommand).FullName);
    }

    [Fact]
    public async Task publish_message_with_null_message_should_throw_and_not_persist()
    {
        var act = () => _sut.PublishMessageAsync(new MessageEnvelope(null));

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await _dbContext.PersistMessage.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task exist_message_should_only_match_processed_inbox_messages()
    {
        var inProgressInbox = new FakeIntegrationEvent(Guid.NewGuid(), "a");
        var processedInbox = new FakeIntegrationEvent(Guid.NewGuid(), "b");
        var processedOutbox = new FakeIntegrationEvent(Guid.NewGuid(), "c");

        await _sut.AddReceivedMessageAsync(new MessageEnvelope(inProgressInbox));
        await _sut.AddReceivedMessageAsync(new MessageEnvelope(processedInbox));
        await _sut.PublishMessageAsync(new MessageEnvelope(processedOutbox));

        await _sut.ProcessInboxAsync(processedInbox.EventId);
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == processedOutbox.EventId)).ChangeState(
            MessageStatus.Processed
        );
        await _dbContext.SaveChangesAsync();

        (await _sut.ExistMessageAsync(inProgressInbox.EventId)).Should().BeNull();
        (await _sut.ExistMessageAsync(processedOutbox.EventId)).Should().BeNull();
        (await _sut.ExistMessageAsync(processedInbox.EventId)).Should().NotBeNull();
    }

    [Fact]
    public async Task process_inbox_should_mark_in_progress_inbox_message_as_processed()
    {
        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "inbox");
        var id = await _sut.AddReceivedMessageAsync(new MessageEnvelope(@event));

        await _sut.ProcessInboxAsync(id);

        var stored = await _dbContext.PersistMessage.SingleAsync(x => x.Id == id);
        stored.MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task process_outbox_message_should_publish_event_with_headers_and_mark_processed()
    {
        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "outbox");
        await _sut.PublishMessageAsync(
            new MessageEnvelope(@event, new Dictionary<string, object?> { ["UserId"] = "7" })
        );

        await _sut.ProcessAsync(@event.EventId, MessageDeliveryType.Outbox);

        await _publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<object>(x => x is FakeIntegrationEvent && ((FakeIntegrationEvent)x).EventId == @event.EventId),
                Arg.Any<IPipe<PublishContext>>(),
                Arg.Any<CancellationToken>()
            );

        var pipe = (IPipe<PublishContext>)_publishEndpoint.ReceivedCalls().Single().GetArguments()[1]!;
        var publishContext = Substitute.For<PublishContext>();
        await pipe.Send(publishContext);
        publishContext.Headers.Received(1).Set("UserId", Arg.Any<object>());

        var stored = await _dbContext.PersistMessage.SingleAsync(x => x.Id == @event.EventId);
        stored.MessageStatus.Should().Be(MessageStatus.Processed);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_internal_message_should_send_command_through_mediator_and_mark_processed()
    {
        var command = new FakeInternalCommand(Guid.NewGuid(), "internal");
        await _sut.AddInternalMessageAsync(command);

        await _sut.ProcessAsync(command.EventId, MessageDeliveryType.Internal);

        await _mediator
            .Received(1)
            .Send(
                Arg.Is<object>(x => x is FakeInternalCommand && ((FakeInternalCommand)x).EventId == command.EventId),
                Arg.Any<CancellationToken>()
            );
        await _publishEndpoint
            .DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());

        var stored = await _dbContext.PersistMessage.SingleAsync(x => x.Id == command.EventId);
        stored.MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task process_outbox_message_whose_payload_is_not_an_event_should_not_publish_and_keep_in_progress()
    {
        var id = Guid.NewGuid();
        var envelope = new MessageEnvelope(new FakeNotAnEvent("plain"));
        _dbContext.PersistMessage.Add(
            new PersistMessage(
                id,
                typeof(FakeNotAnEvent).FullName!,
                JsonSerializer.Serialize(envelope),
                MessageDeliveryType.Outbox
            )
        );
        await _dbContext.SaveChangesAsync();

        await _sut.ProcessAsync(id, MessageDeliveryType.Outbox);

        await _publishEndpoint
            .DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == id))
            .MessageStatus.Should()
            .Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task process_internal_message_whose_payload_is_not_an_internal_command_should_not_send_and_keep_in_progress()
    {
        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "not-a-command");
        _dbContext.PersistMessage.Add(
            new PersistMessage(
                @event.EventId,
                typeof(FakeIntegrationEvent).FullName!,
                JsonSerializer.Serialize(new MessageEnvelope(@event)),
                MessageDeliveryType.Internal
            )
        );
        await _dbContext.SaveChangesAsync();

        await _sut.ProcessAsync(@event.EventId, MessageDeliveryType.Internal);

        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == @event.EventId))
            .MessageStatus.Should()
            .Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task process_message_with_null_envelope_message_should_keep_in_progress()
    {
        var id = Guid.NewGuid();
        _dbContext.PersistMessage.Add(
            new PersistMessage(
                id,
                typeof(FakeIntegrationEvent).FullName!,
                JsonSerializer.Serialize(new MessageEnvelope(null)),
                MessageDeliveryType.Outbox
            )
        );
        await _dbContext.SaveChangesAsync();

        await _sut.ProcessAsync(id, MessageDeliveryType.Outbox);

        await _publishEndpoint
            .DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == id))
            .MessageStatus.Should()
            .Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task process_message_that_does_not_exist_should_do_nothing()
    {
        await _sut.ProcessAsync(Guid.NewGuid(), MessageDeliveryType.Outbox);

        await _publishEndpoint
            .DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_message_with_mismatched_delivery_type_should_do_nothing()
    {
        var @event = new FakeIntegrationEvent(Guid.NewGuid(), "outbox");
        await _sut.PublishMessageAsync(new MessageEnvelope(@event));

        await _sut.ProcessAsync(@event.EventId, MessageDeliveryType.Internal);

        await _publishEndpoint
            .DidNotReceiveWithAnyArgs()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == @event.EventId))
            .MessageStatus.Should()
            .Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task process_all_should_only_process_messages_that_are_not_already_processed()
    {
        var pending = new FakeIntegrationEvent(Guid.NewGuid(), "pending");
        var alreadyProcessed = new FakeIntegrationEvent(Guid.NewGuid(), "done");
        var pendingCommand = new FakeInternalCommand(Guid.NewGuid(), "cmd");

        await _sut.PublishMessageAsync(new MessageEnvelope(pending));
        await _sut.PublishMessageAsync(new MessageEnvelope(alreadyProcessed));
        await _sut.AddInternalMessageAsync(pendingCommand);
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == alreadyProcessed.EventId)).ChangeState(
            MessageStatus.Processed
        );
        await _dbContext.SaveChangesAsync();

        await _sut.ProcessAllAsync();

        await _publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<object>(x => ((FakeIntegrationEvent)x).EventId == pending.EventId),
                Arg.Any<IPipe<PublishContext>>(),
                Arg.Any<CancellationToken>()
            );
        await _publishEndpoint
            .DidNotReceive()
            .Publish(
                Arg.Is<object>(x => ((FakeIntegrationEvent)x).EventId == alreadyProcessed.EventId),
                Arg.Any<IPipe<PublishContext>>(),
                Arg.Any<CancellationToken>()
            );
        await _mediator.Received(1).Send(Arg.Any<object>(), Arg.Any<CancellationToken>());

        (await _dbContext.PersistMessage.ToListAsync())
            .Should()
            .OnlyContain(x => x.MessageStatus == MessageStatus.Processed);
    }

    [Fact]
    public async Task get_by_filter_should_return_matching_messages()
    {
        await _sut.PublishMessageAsync(new MessageEnvelope(new FakeIntegrationEvent(Guid.NewGuid(), "a")));
        await _sut.AddReceivedMessageAsync(new MessageEnvelope(new FakeIntegrationEvent(Guid.NewGuid(), "b")));

        var result = await _sut.GetByFilterAsync(x => x.DeliveryType == MessageDeliveryType.Inbox);

        result.Should().ContainSingle().Which.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
    }
}
