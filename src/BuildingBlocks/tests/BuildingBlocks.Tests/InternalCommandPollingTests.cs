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

public class InternalCommandPollingTests
{
    [Theory]
    [InlineData("Missing.Namespace.RemovedCommand")]
    [InlineData(typeof(NonCommand))]
    public async Task polling_leaves_unsupported_rows_pending_and_dispatches_valid_commands(object unsupportedType)
    {
        await using var store = new PersistMessageDbContext<InternalCommandPollingTests>(
            new DbContextOptionsBuilder<PersistMessageDbContext<InternalCommandPollingTests>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var mediator = Substitute.For<IMediator>();
        var processor = new PersistMessageProcessor<InternalCommandPollingTests>(
            NullLogger<PersistMessageProcessor<InternalCommandPollingTests>>.Instance,
            mediator,
            store,
            Substitute.For<IPublishEndpoint>()
        );
        var unsupported = new PersistMessage(
            Guid.NewGuid(),
            unsupportedType.ToString()!,
            "not json",
            MessageDeliveryType.Internal
        );
        store.PersistMessage.Add(unsupported);
        await store.SaveChangesAsync();
        var command = new PollingCommand(Guid.NewGuid());
        await processor.AddInternalMessageAsync(command);

        await processor.ProcessAllAsync();

        mediator.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0].Should().Be(command);
        var rows = await store.PersistMessage.AsNoTracking().ToListAsync();
        rows.Single(row => row.Id == unsupported.Id).MessageStatus.Should().Be(MessageStatus.InProgress);
        rows.Single(row => row.Id != unsupported.Id).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    public sealed record NonCommand(Guid Id);

    public sealed record PollingCommand(Guid Id) : InternalCommand;
}
