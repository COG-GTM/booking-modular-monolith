using System.Transactions;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Modules;

public sealed class EfTxPassengerBehaviorTests : IDisposable
{
    private readonly TransactionBehaviorFixture _fixture = new();
    private readonly string _databaseName = DbContextFactory.NewDatabaseName();
    private readonly PassengerDbContext _dbContext;
    private readonly FakeCommand _request = new("passenger");

    public EfTxPassengerBehaviorTests()
    {
        _dbContext = ModuleFakes.CreatePassengerDbContext(_databaseName);
    }

    public void Dispose() => _dbContext.Dispose();

    private EfTxPassengerBehavior<FakeCommand, string> CreateSut()
    {
        return new EfTxPassengerBehavior<FakeCommand, string>(
            Substitute.For<ILogger<EfTxPassengerBehavior<FakeCommand, string>>>(),
            _dbContext,
            _fixture.PersistMessageDbContext,
            _fixture.EventDispatcher
        );
    }

    [Fact]
    public async Task handle_without_domain_events_should_return_response_without_dispatching_or_saving()
    {
        var response = await CreateSut()
            .Handle(_request, TransactionBehaviorFixture.Handler("handled"), CancellationToken.None);

        response.Should().Be("handled");
        await _fixture
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handle_should_dispatch_passenger_domain_events_and_persist_passenger_inside_committed_transaction()
    {
        var passenger = ModuleFakes.CreatePassenger();

        var response = await CreateSut()
            .Handle(
                _request,
                TransactionBehaviorFixture.Handler("created", () => _dbContext.Passengers.Add(passenger)),
                CancellationToken.None
            );

        response.Should().Be("created");
        await _fixture
            .EventDispatcher.Received(1)
            .SendAsync(
                Arg.Is<IReadOnlyList<IDomainEvent>>(e =>
                    e.Count == 1 && ((PassengerCreatedDomainEvent)e[0]).Id == passenger.Id.Value
                ),
                typeof(FakeCommand),
                Arg.Any<CancellationToken>()
            );
        await _fixture.PersistMessageDbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        passenger.DomainEvents.Should().BeEmpty();

        var transactionId = _fixture.DispatcherTransactionIds.Should().ContainSingle().Which;
        transactionId.Should().NotBeNull();
        _fixture.PersistSaveTransactionIds.Should().Equal(transactionId);
        _fixture.CompletedTransactions[transactionId!].Should().Be(TransactionStatus.Committed);

        await using var verification = ModuleFakes.CreatePassengerDbContext(_databaseName);
        (await verification.Passengers.SingleAsync()).Id.Should().Be(passenger.Id);
    }

    [Fact]
    public async Task handle_should_abort_transaction_and_not_persist_passenger_when_event_dispatch_fails()
    {
        _fixture.FailEventDispatcher(new InvalidOperationException("dispatch failed"));
        var passenger = ModuleFakes.CreatePassenger();

        var act = () =>
            CreateSut()
                .Handle(
                    _request,
                    TransactionBehaviorFixture.Handler("created", () => _dbContext.Passengers.Add(passenger)),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("dispatch failed");
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        var transactionId = _fixture.DispatcherTransactionIds.Should().ContainSingle().Which;
        _fixture.CompletedTransactions[transactionId!].Should().Be(TransactionStatus.Aborted);
        Transaction.Current.Should().BeNull();

        await using var verification = ModuleFakes.CreatePassengerDbContext(_databaseName);
        (await verification.Passengers.AnyAsync()).Should().BeFalse();
    }
}
