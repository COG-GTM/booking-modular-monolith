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

public class PersistMessageIdentityTests
{
    [Fact]
    public async Task outbox_identity_uses_event_id_and_ignores_message_id_header()
    {
        await using var store = CreateStore<First>();
        var processor = CreateProcessor<First>(store);
        var message = new TestEvent(Guid.NewGuid());
        var headerId = Guid.NewGuid();

        await processor.PublishMessageAsync(
            new MessageEnvelope(message, new Dictionary<string, object?> { ["MessageId"] = headerId })
        );

        var row = await store.PersistMessage.SingleAsync();
        row.Id.Should().Be(message.EventId);
        row.DeliveryType.Should().Be(MessageDeliveryType.Outbox);
        row.MessageStatus.Should().Be(MessageStatus.InProgress);
    }

    [Fact]
    public async Task inbox_identity_falls_back_to_event_id_for_invalid_message_id_header()
    {
        await using var store = CreateStore<First>();
        var processor = CreateProcessor<First>(store);
        var message = new TestEvent(Guid.NewGuid());

        await processor.AddReceivedMessageAsync(
            new MessageEnvelope(message, new Dictionary<string, object?> { ["MessageId"] = "not-a-guid" })
        );

        (await store.PersistMessage.SingleAsync()).Id.Should().Be(message.EventId);
    }

    [Fact]
    public async Task inbox_identity_uses_valid_message_id_and_deduplicates_in_store()
    {
        await using var store = CreateStore<First>();
        var processor = CreateProcessor<First>(store);
        var messageId = Guid.NewGuid();

        var firstId = await processor.AddReceivedMessageAsync(
            new MessageEnvelope(
                new TestEvent(Guid.NewGuid()),
                new Dictionary<string, object?> { ["MessageId"] = messageId.ToString() }
            )
        );
        var secondId = await processor.AddReceivedMessageAsync(
            new MessageEnvelope(
                new TestEvent(Guid.NewGuid()),
                new Dictionary<string, object?> { ["MessageId"] = messageId.ToString() }
            )
        );

        firstId.Should().Be(messageId);
        secondId.Should().Be(messageId);
        (await store.PersistMessage.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task internal_command_uses_event_id_and_internal_delivery_type()
    {
        await using var store = CreateStore<First>();
        var command = new TestCommand(Guid.NewGuid());

        await CreateProcessor<First>(store).AddInternalMessageAsync(command);

        var row = await store.PersistMessage.SingleAsync();
        row.Id.Should().Be(command.EventId);
        row.DeliveryType.Should().Be(MessageDeliveryType.Internal);
    }

    [Fact]
    public async Task inbox_deduplication_is_scoped_to_each_module_store()
    {
        await using var firstStore = CreateStore<First>();
        await using var secondStore = CreateStore<Second>();
        var firstProcessor = CreateProcessor<First>(firstStore);
        var secondProcessor = CreateProcessor<Second>(secondStore);
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope(
            new TestEvent(Guid.NewGuid()),
            new Dictionary<string, object?> { ["MessageId"] = messageId }
        );

        (await firstProcessor.AddReceivedMessageAsync(envelope)).Should().Be(messageId);
        (await secondProcessor.AddReceivedMessageAsync(envelope)).Should().Be(messageId);

        (await firstStore.PersistMessage.SingleAsync()).Id.Should().Be(messageId);
        (await secondStore.PersistMessage.SingleAsync()).Id.Should().Be(messageId);
    }

    private static PersistMessageDbContext<TModule> CreateStore<TModule>() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext<TModule>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static PersistMessageProcessor<TModule> CreateProcessor<TModule>(PersistMessageDbContext<TModule> store) =>
        new(
            NullLogger<PersistMessageProcessor<TModule>>.Instance,
            Substitute.For<IMediator>(),
            store,
            Substitute.For<IPublishEndpoint>()
        );

    public sealed class First;

    public sealed class Second;

    private sealed record TestEvent(Guid EventId) : IIntegrationEvent;

    private sealed record TestCommand(Guid EventId) : IInternalCommand;
}
