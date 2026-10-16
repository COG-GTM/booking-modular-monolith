using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class PersistMessageProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public PersistMessageProcessorTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private PersistMessageDbContext<PersistFlightModule> CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<PersistFlightModule>>()
            .UseSqlite(_connection)
            .Options;
        var context = new PersistMessageDbContext<PersistFlightModule>(options);
        context.Database.EnsureCreated();
        return context;
    }

    private (
        PersistMessageProcessor<PersistFlightModule> Processor,
        PersistMessageDbContext<PersistFlightModule> DbContext,
        IMediator Mediator,
        IPublishEndpoint PublishEndpoint
    ) CreateProcessor()
    {
        var dbContext = CreateDbContext();
        var mediator = Substitute.For<IMediator>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var processor = new PersistMessageProcessor<PersistFlightModule>(
            NullLogger<PersistMessageProcessor<PersistFlightModule>>.Instance,
            mediator,
            dbContext,
            publishEndpoint
        );
        return (processor, dbContext, mediator, publishEndpoint);
    }

    [Fact]
    public async Task publish_message_async_stores_outbox_row_with_serialized_envelope()
    {
        var (processor, dbContext, _, _) = CreateProcessor();
        var integrationEvent = new PersistItemCreatedIntegrationEvent(Guid.NewGuid());
        var envelope = new MessageEnvelope(integrationEvent, new Dictionary<string, object?> { ["tenant"] = "acme" });

        await processor.PublishMessageAsync(envelope);

        var row = await dbContext.PersistMessage.SingleAsync();
        row.DataType.Should().Be(typeof(PersistItemCreatedIntegrationEvent).FullName);
        row.MessageStatus.Should().Be(MessageStatus.InProgress);
        row.DeliveryType.Should().Be(MessageDeliveryType.Outbox);
        var stored = JsonSerializer.Deserialize<MessageEnvelope>(row.Data);
        stored.Should().NotBeNull();
        stored!.Headers["tenant"].ToString().Should().Be("acme");
    }

    [Fact]
    public async Task add_received_message_with_explicit_id_uses_that_id()
    {
        var (processor, dbContext, _, _) = CreateProcessor();
        var id = Guid.NewGuid();
        var envelope = new MessageEnvelope(new PersistPing(Guid.NewGuid()));

        var returned = await processor.AddReceivedMessageAsync(id, envelope);

        returned.Should().Be(id);
        var row = await dbContext.PersistMessage.SingleAsync();
        row.Id.Should().Be(id);
        row.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
    }

    [Fact]
    public async Task add_received_message_without_id_generates_one_for_non_event_messages()
    {
        var (processor, dbContext, _, _) = CreateProcessor();
        var envelope = new MessageEnvelope(new PersistPlainPayload("x"));

        var id = await processor.AddReceivedMessageAsync(envelope);

        id.Should().NotBe(Guid.Empty);
        (await dbContext.PersistMessage.SingleAsync(m => m.Id == id))
            .DeliveryType.Should()
            .Be(MessageDeliveryType.Inbox);
    }

    [Fact]
    public async Task add_internal_message_stores_internal_row()
    {
        var (processor, dbContext, _, _) = CreateProcessor();

        await processor.AddInternalMessageAsync(new PersistCreateReadModel(Guid.NewGuid()));

        var row = await dbContext.PersistMessage.SingleAsync();
        row.DeliveryType.Should().Be(MessageDeliveryType.Internal);
        row.MessageStatus.Should().Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task null_envelope_message_throws_argument_null()
    {
        var (processor, _, _, _) = CreateProcessor();
        var envelope = new MessageEnvelope(null);

        var act = () => processor.PublishMessageAsync(envelope);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task exist_message_async_returns_only_processed_inbox_rows()
    {
        var (processor, dbContext, _, _) = CreateProcessor();
        var inboxId = Guid.NewGuid();
        await processor.AddReceivedMessageAsync(inboxId, new MessageEnvelope(new PersistPing(Guid.NewGuid())));
        var outboxEvent = new PersistItemCreatedIntegrationEvent(Guid.NewGuid());
        await processor.PublishMessageAsync(new MessageEnvelope(outboxEvent));

        (await processor.ExistMessageAsync(inboxId)).Should().BeNull();

        var inboxRow = await dbContext.PersistMessage.SingleAsync(m => m.Id == inboxId);
        inboxRow.ChangeState(MessageStatus.Processed);
        await dbContext.SaveChangesAsync();

        (await processor.ExistMessageAsync(inboxId)).Should().NotBeNull();
        var outboxRow = await dbContext.PersistMessage.SingleAsync(m => m.DeliveryType == MessageDeliveryType.Outbox);
        outboxRow.ChangeState(MessageStatus.Processed);
        await dbContext.SaveChangesAsync();
        (await processor.ExistMessageAsync(outboxRow.Id)).Should().BeNull();
    }

    [Fact]
    public async Task get_by_filter_async_filters_rows()
    {
        var (processor, dbContext, _, _) = CreateProcessor();
        var id = Guid.NewGuid();
        await processor.AddReceivedMessageAsync(id, new MessageEnvelope(new PersistPing(Guid.NewGuid())));
        await processor.AddInternalMessageAsync(new PersistCreateReadModel(Guid.NewGuid()));

        var inboxRows = await processor.GetByFilterAsync(m => m.DeliveryType == MessageDeliveryType.Inbox);

        inboxRows.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task process_inbox_marks_in_progress_row_processed_and_bumps_version()
    {
        var (processor, dbContext, _, _) = CreateProcessor();
        var id = Guid.NewGuid();
        await processor.AddReceivedMessageAsync(id, new MessageEnvelope(new PersistPing(Guid.NewGuid())));
        var before = (await dbContext.PersistMessage.SingleAsync(m => m.Id == id)).Version;

        await processor.ProcessInboxAsync(id);

        dbContext.ChangeTracker.Clear();
        var row = await dbContext.PersistMessage.SingleAsync(m => m.Id == id);
        row.MessageStatus.Should().Be(MessageStatus.Processed);
        row.Version.Should().Be(before + 1);
    }

    [Fact]
    public async Task process_async_outbox_publishes_event_with_row_id_and_custom_headers()
    {
        var (processor, dbContext, _, publishEndpoint) = CreateProcessor();
        var integrationEvent = new PersistItemCreatedIntegrationEvent(Guid.NewGuid());
        var envelope = new MessageEnvelope(
            integrationEvent,
            new Dictionary<string, object?>
            {
                ["traceparent"] = "00-abc-01",
                ["tracestate"] = "state=1",
                ["tenant"] = "acme",
            }
        );
        await processor.PublishMessageAsync(envelope);
        var row = await dbContext.PersistMessage.SingleAsync();

        IPipe<PublishContext>? captured = null;
        publishEndpoint
            .Publish(
                Arg.Any<object>(),
                Arg.Do<IPipe<PublishContext>>(pipe => captured = pipe),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<object>(o => o.GetType() == typeof(PersistItemCreatedIntegrationEvent)),
                Arg.Any<IPipe<PublishContext>>(),
                Arg.Any<CancellationToken>()
            );
        captured.Should().NotBeNull();
        var publishContext = Substitute.For<PublishContext>();
        await captured!.Send(publishContext);
        publishContext.Received().MessageId = row.Id;
        publishContext.Headers.Received().Set("tenant", Arg.Any<object>());
        publishContext.Headers.DidNotReceive().Set("traceparent", Arg.Any<object>());
        publishContext.Headers.DidNotReceive().Set("tracestate", Arg.Any<object>());

        dbContext.ChangeTracker.Clear();
        (await dbContext.PersistMessage.SingleAsync(m => m.Id == row.Id))
            .MessageStatus.Should()
            .Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task process_async_outbox_leaves_non_event_payload_in_progress()
    {
        var (processor, dbContext, _, publishEndpoint) = CreateProcessor();
        var plain = new PersistPlainPayload("x");
        // Manually stage an outbox row whose payload is not an IEvent.
        var envelope = new MessageEnvelope(plain);
        dbContext.PersistMessage.Add(
            new PersistMessage(
                Guid.NewGuid(),
                typeof(PersistPlainPayload).FullName!,
                JsonSerializer.Serialize(envelope),
                MessageDeliveryType.Outbox
            )
        );
        await dbContext.SaveChangesAsync();
        var row = await dbContext.PersistMessage.SingleAsync();

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        await publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
        dbContext.ChangeTracker.Clear();
        (await dbContext.PersistMessage.SingleAsync(m => m.Id == row.Id))
            .MessageStatus.Should()
            .Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task process_async_internal_sends_command_via_mediator()
    {
        var (processor, dbContext, mediator, _) = CreateProcessor();
        var command = new PersistCreateReadModel(Guid.NewGuid());
        await processor.AddInternalMessageAsync(command);
        var row = await dbContext.PersistMessage.SingleAsync();

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Internal);

        await mediator
            .Received(1)
            .Send(Arg.Is<object>(o => o.GetType() == typeof(PersistCreateReadModel)), Arg.Any<CancellationToken>());
        dbContext.ChangeTracker.Clear();
        (await dbContext.PersistMessage.SingleAsync(m => m.Id == row.Id))
            .MessageStatus.Should()
            .Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task process_async_no_ops_for_unknown_id_or_delivery_type_mismatch()
    {
        var (processor, _, mediator, publishEndpoint) = CreateProcessor();

        await processor.ProcessAsync(Guid.NewGuid(), MessageDeliveryType.Outbox);

        var internalCommand = new PersistCreateReadModel(Guid.NewGuid());
        await processor.AddInternalMessageAsync(internalCommand);
        var internalId = (await processor.GetByFilterAsync(m => m.DeliveryType == MessageDeliveryType.Internal))
            .Single()
            .Id;
        await processor.ProcessAsync(internalId, MessageDeliveryType.Outbox);

        await mediator.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_all_processes_outbox_and_internal_but_not_inbox()
    {
        var (processor, dbContext, mediator, publishEndpoint) = CreateProcessor();
        await processor.PublishMessageAsync(
            new MessageEnvelope(new PersistItemCreatedIntegrationEvent(Guid.NewGuid()))
        );
        await processor.AddInternalMessageAsync(new PersistCreateReadModel(Guid.NewGuid()));
        await processor.AddReceivedMessageAsync(new MessageEnvelope(new PersistPing(Guid.NewGuid())));

        await processor.ProcessAllAsync();

        dbContext.ChangeTracker.Clear();
        var rows = await dbContext.PersistMessage.ToListAsync();
        rows.Single(m => m.DeliveryType == MessageDeliveryType.Outbox)
            .MessageStatus.Should()
            .Be(MessageStatus.Processed);
        rows.Single(m => m.DeliveryType == MessageDeliveryType.Internal)
            .MessageStatus.Should()
            .Be(MessageStatus.Processed);
        rows.Single(m => m.DeliveryType == MessageDeliveryType.Inbox)
            .MessageStatus.Should()
            .Be(MessageStatus.InProgress);
    }
}
