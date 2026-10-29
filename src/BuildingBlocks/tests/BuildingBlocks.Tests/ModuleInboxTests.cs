using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class ModuleInboxTests
{
    [Fact]
    public async Task duplicate_transport_message_id_is_processed_once_in_the_owning_store()
    {
        await using var store = CreateStore();
        var processor = CreateProcessor(store);
        var filter = new ConsumeFilter<ModuleInboxTests, Received>(processor);
        var id = Guid.NewGuid();
        var context = Context(id);
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();
        await filter.Send(context, next);
        await filter.Send(Context(id), next);
        await next.Received(1).Send(Arg.Any<ConsumeContext<Received>>());
        var row = await store.PersistMessage.SingleAsync();
        row.Id.Should().Be(InboxMessageId.For(context));
        row.MessageStatus.Should().Be(MessageStatus.Processed);
        row.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
    }

    [Fact]
    public async Task failed_consumer_leaves_inbox_retryable()
    {
        await using var store = CreateStore();
        var filter = new ConsumeFilter<ModuleInboxTests, Received>(CreateProcessor(store));
        var context = Context(Guid.NewGuid());
        var failing = Substitute.For<IPipe<ConsumeContext<Received>>>();
        failing.Send(context).Returns(Task.FromException(new InvalidOperationException("consumer failed")));
        var act = () => filter.Send(context, failing);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("consumer failed");
        (await store.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.InProgress);
        var retry = Substitute.For<IPipe<ConsumeContext<Received>>>();
        await filter.Send(context, retry);
        await retry.Received(1).Send(context);
        (await store.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task empty_inbox_does_not_find_a_processed_message()
    {
        await using var store = CreateStore();
        (await CreateProcessor(store).ExistMessageAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static PersistMessageDbContext<ModuleInboxTests> CreateStore() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext<ModuleInboxTests>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static PersistMessageProcessor<ModuleInboxTests> CreateProcessor(
        PersistMessageDbContext<ModuleInboxTests> store
    ) =>
        new(
            NullLogger<PersistMessageProcessor<ModuleInboxTests>>.Instance,
            Substitute.For<IMediator>(),
            store,
            Substitute.For<IPublishEndpoint>()
        );

    private static ConsumeContext<Received> Context(Guid id)
    {
        var context = Substitute.For<ConsumeContext<Received>>();
        context.MessageId.Returns(id);
        context.Message.Returns(new Received(Guid.NewGuid()));
        var headers = Substitute.For<Headers>();
        headers.GetEnumerator().Returns(_ => Enumerable.Empty<HeaderValue>().GetEnumerator());
        context.Headers.Returns(headers);
        return context;
    }

    public sealed record Received(Guid Id) : IIntegrationEvent;
}
