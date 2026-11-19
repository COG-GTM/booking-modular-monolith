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

public class PersistMessagePollingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task polling_never_dispatches_or_completes_inbox_rows(bool hasInbox)
    {
        await using var store = new PersistMessageDbContext<PersistMessagePollingTests>(
            new DbContextOptionsBuilder<PersistMessageDbContext<PersistMessagePollingTests>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var publisher = Substitute.For<IPublishEndpoint>();
        var mediator = Substitute.For<IMediator>();
        var processor = new PersistMessageProcessor<PersistMessagePollingTests>(
            NullLogger<PersistMessageProcessor<PersistMessagePollingTests>>.Instance,
            mediator,
            store,
            publisher
        );
        if (hasInbox)
            await processor.AddReceivedMessageAsync(new MessageEnvelope(new Received(Guid.NewGuid())));

        await processor.ProcessAllAsync();

        publisher.ReceivedCalls().Should().BeEmpty();
        mediator.ReceivedCalls().Should().BeEmpty();
        var rows = await store.PersistMessage.ToListAsync();
        rows.Should().HaveCount(hasInbox ? 1 : 0);
        rows.Should().OnlyContain(x => x.MessageStatus == MessageStatus.InProgress);
    }

    public sealed record Received(Guid Id) : IIntegrationEvent;
}
