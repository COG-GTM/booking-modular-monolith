using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class EfTxBehaviorTests
{
    private sealed record PersistTxCommand : IRequest<string>;

    private static EfTxBehavior<PersistTxCommand, string> CreateBehavior(
        IDbContext dbContext,
        IPersistMessageDbContext persistMessageDbContext,
        IEventDispatcher eventDispatcher
    ) =>
        new(
            NullLogger<EfTxBehavior<PersistTxCommand, string>>.Instance,
            dbContext,
            persistMessageDbContext,
            eventDispatcher
        );

    [Fact]
    public async Task no_domain_events_returns_response_without_dispatch_or_save()
    {
        var dbContext = Substitute.For<IDbContext>();
        dbContext.GetDomainEvents().Returns(Array.Empty<IDomainEvent>());
        var persistDbContext = Substitute.For<IPersistMessageDbContext>();
        var dispatcher = Substitute.For<IEventDispatcher>();
        var behavior = CreateBehavior(dbContext, persistDbContext, dispatcher);

        var response = await behavior.Handle(
            new PersistTxCommand(),
            _ => Task.FromResult("done"),
            CancellationToken.None
        );

        response.Should().Be("done");
        await dispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await persistDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task domain_events_are_dispatched_then_both_contexts_saved()
    {
        var domainEvent = new PersistOrderCreated(Guid.NewGuid());
        var dbContext = Substitute.For<IDbContext>();
        dbContext.GetDomainEvents().Returns(new List<IDomainEvent> { domainEvent });
        var persistDbContext = Substitute.For<IPersistMessageDbContext>();
        var dispatcher = Substitute.For<IEventDispatcher>();
        var behavior = CreateBehavior(dbContext, persistDbContext, dispatcher);

        var response = await behavior.Handle(
            new PersistTxCommand(),
            _ => Task.FromResult("done"),
            CancellationToken.None
        );

        response.Should().Be("done");
        await dispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<IReadOnlyList<IDomainEvent>>(e => e.Contains(domainEvent)),
                typeof(PersistTxCommand),
                Arg.Any<CancellationToken>()
            );
        await dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await persistDbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
