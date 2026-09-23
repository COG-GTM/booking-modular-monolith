using System.Transactions;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.EFCore;

public class EfTxBehaviorTests
{
    private readonly TransactionBehaviorFixture _fixture = new();
    private readonly IDbContext _dbContext = Substitute.For<IDbContext>();
    private readonly List<string?> _moduleSaveTransactionIds = new();
    private readonly FakeCommand _request = new("tx");

    public EfTxBehaviorTests()
    {
        _dbContext
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _fixture.ObserveAmbientTransaction(_moduleSaveTransactionIds);
                return Task.FromResult(1);
            });
    }

    private EfTxBehavior<FakeCommand, string> CreateSut()
    {
        return new EfTxBehavior<FakeCommand, string>(
            Substitute.For<ILogger<EfTxBehavior<FakeCommand, string>>>(),
            _dbContext,
            _fixture.PersistMessageDbContext,
            _fixture.EventDispatcher
        );
    }

    private void GivenDomainEvents(params IDomainEvent[] events)
    {
        _dbContext.GetDomainEvents().Returns(events);
    }

    [Fact]
    public async Task handle_without_domain_events_should_return_response_without_dispatching_or_saving()
    {
        GivenDomainEvents();

        var response = await CreateSut()
            .Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        response.Should().Be("handled");
        await _fixture
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await _dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handle_with_domain_events_should_dispatch_them_and_save_both_contexts_inside_one_committed_transaction()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid(), "created");
        GivenDomainEvents(domainEvent);

        var response = await CreateSut()
            .Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        response.Should().Be("handled");
        await _fixture
            .EventDispatcher.Received(1)
            .SendAsync(
                Arg.Is<IReadOnlyList<IDomainEvent>>(e => e.Count == 1 && ReferenceEquals(e[0], domainEvent)),
                typeof(FakeCommand),
                Arg.Any<CancellationToken>()
            );
        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var transactionId = _fixture.DispatcherTransactionIds.Should().ContainSingle().Which;
        transactionId.Should().NotBeNull();
        _moduleSaveTransactionIds.Should().Equal(transactionId);
        _fixture.PersistSaveTransactionIds.Should().Equal(transactionId);
        _fixture.CompletedTransactions[transactionId!].Should().Be(TransactionStatus.Committed);
        Transaction.Current.Should().BeNull();
    }

    [Fact]
    public async Task handler_failure_should_propagate_and_skip_dispatch_and_saves()
    {
        GivenDomainEvents(new FakeDomainEvent(Guid.NewGuid(), "never"));

        var act = () =>
            CreateSut()
                .Handle(
                    _request,
                    TransactionBehaviorFixture.FailingHandler<string>(new InvalidOperationException("handler failed")),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        await _fixture
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await _dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Transaction.Current.Should().BeNull();
    }

    [Fact]
    public async Task event_dispatch_failure_should_propagate_skip_saves_and_abort_transaction()
    {
        GivenDomainEvents(new FakeDomainEvent(Guid.NewGuid(), "boom"));
        _fixture.FailEventDispatcher(new InvalidOperationException("dispatch failed"));

        var act = () =>
            CreateSut().Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatch failed");
        await _dbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        var transactionId = _fixture.DispatcherTransactionIds.Should().ContainSingle().Which;
        _fixture.CompletedTransactions[transactionId!].Should().Be(TransactionStatus.Aborted);
    }

    [Fact]
    public async Task module_save_failure_should_propagate_skip_outbox_save_and_abort_transaction()
    {
        GivenDomainEvents(new FakeDomainEvent(Guid.NewGuid(), "boom"));
        _dbContext
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _fixture.ObserveAmbientTransaction(_moduleSaveTransactionIds);
                return Task.FromException<int>(new InvalidOperationException("save failed"));
            });

        var act = () =>
            CreateSut().Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("save failed");
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _moduleSaveTransactionIds.Should().NotBeEmpty().And.OnlyContain(id => id != null);
        _fixture.CompletedTransactions[_moduleSaveTransactionIds[0]!].Should().Be(TransactionStatus.Aborted);
    }

    [Fact]
    public async Task outbox_save_failure_should_propagate_and_abort_transaction()
    {
        GivenDomainEvents(new FakeDomainEvent(Guid.NewGuid(), "boom"));
        _fixture.FailPersistMessageSave(new InvalidOperationException("outbox save failed"));

        var act = () =>
            CreateSut().Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outbox save failed");
        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _fixture.PersistSaveTransactionIds.Should().NotBeEmpty().And.OnlyContain(id => id != null);
        _fixture.CompletedTransactions[_fixture.PersistSaveTransactionIds[0]!].Should().Be(TransactionStatus.Aborted);
    }
}
