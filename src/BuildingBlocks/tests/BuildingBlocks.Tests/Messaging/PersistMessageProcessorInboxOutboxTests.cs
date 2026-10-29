using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using PersistMessageProcessorService = BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor<BuildingBlocks.Tests.Messaging.MessagingTestModule>;

namespace BuildingBlocks.Tests.Messaging;

public class PersistMessageProcessorInboxOutboxTests
{
    [Fact]
    public async Task received_message_uses_the_inbox_id_and_starts_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var processor = CreateProcessor(dbContext);
        var eventMessage = new PersistMessageTestEvent(Guid.NewGuid());
        var inboxId = Guid.NewGuid();

        var recorded = await processor.TryAddReceivedMessageAsync(new MessageEnvelope(eventMessage), inboxId);

        var stored = await dbContext.PersistMessage.SingleAsync();
        recorded.Should().BeTrue();
        stored.Id.Should().Be(inboxId);
        stored.Id.Should().NotBe(eventMessage.EventId);
        stored.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
        stored.MessageStatus.Should().Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task inbox_lookup_returns_only_rows_with_the_requested_inbox_id()
    {
        await using var dbContext = CreateDbContext();
        var processor = CreateProcessor(dbContext);
        var inboxId = Guid.NewGuid();

        await processor.TryAddReceivedMessageAsync(
            new MessageEnvelope(new PersistMessageTestEvent(Guid.NewGuid())),
            inboxId
        );
        await processor.PublishMessageAsync(new MessageEnvelope(new PersistMessageTestEvent(Guid.NewGuid())));
        var outboxId = await dbContext
            .PersistMessage.Where(message => message.DeliveryType == MessageDeliveryType.Outbox)
            .Select(message => message.Id)
            .SingleAsync();

        (await processor.GetInboxMessageAsync(inboxId)).Should().NotBeNull();
        (await processor.GetInboxMessageAsync(Guid.NewGuid())).Should().BeNull();
        (await processor.GetInboxMessageAsync(outboxId)).Should().BeNull();
    }

    [Fact]
    public async Task processing_inbox_marks_it_processed_and_unknown_ids_are_ignored()
    {
        await using var dbContext = CreateDbContext();
        var processor = CreateProcessor(dbContext);
        var inboxId = Guid.NewGuid();

        await processor.TryAddReceivedMessageAsync(
            new MessageEnvelope(new PersistMessageTestEvent(Guid.NewGuid())),
            inboxId
        );
        await processor.ProcessInboxAsync(inboxId);

        (await dbContext.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
        var act = () => processor.ProcessInboxAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task outbox_publish_uses_the_row_id_and_copies_headers()
    {
        await using var dbContext = CreateDbContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        IPipe<PublishContext>? publishPipe = null;
        publishEndpoint
            .Publish(
                Arg.Any<object>(),
                Arg.Do<IPipe<PublishContext>>(pipe => publishPipe = pipe),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var processor = CreateProcessor(dbContext, publishEndpoint);
        var correlationId = Guid.NewGuid().ToString();
        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new PersistMessageTestEvent(Guid.NewGuid()),
                new Dictionary<string, object?> { ["CorrelationId"] = correlationId }
            )
        );
        var outboxRow = await dbContext.PersistMessage.SingleAsync();

        await processor.ProcessAsync(outboxRow.Id, MessageDeliveryType.Outbox);

        publishPipe.Should().NotBeNull();
        var publishContext = Substitute.For<PublishContext>();
        var headers = Substitute.For<SendHeaders>();
        publishContext.Headers.Returns(headers);
        await publishPipe!.Send(publishContext);

        publishContext.Received(1).MessageId = outboxRow.Id;
        headers
            .Received(1)
            .Set(
                "CorrelationId",
                Arg.Is<object?>(value => value != null && value.ToString() == correlationId),
                Arg.Any<bool>()
            );
        (await dbContext.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    private static PersistMessageDbContext<MessagingTestModule> CreateDbContext() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext<MessagingTestModule>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static PersistMessageProcessorService CreateProcessor(
        PersistMessageDbContext<MessagingTestModule> dbContext,
        IPublishEndpoint? publishEndpoint = null
    ) =>
        new(
            NullLogger<PersistMessageProcessorService>.Instance,
            Substitute.For<IMediator>(),
            dbContext,
            publishEndpoint ?? Substitute.For<IPublishEndpoint>()
        );
}

public record PersistMessageTestEvent(Guid EventId) : IEvent;
