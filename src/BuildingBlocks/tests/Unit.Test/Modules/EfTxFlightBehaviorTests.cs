using System.Transactions;
using BuildingBlocks.Core.Event;
using Flight.Data;
using Flight.Flights.Features.CreatingFlight.V1;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Modules;

public sealed class EfTxFlightBehaviorTests : IDisposable
{
    private readonly TransactionBehaviorFixture _fixture = new();
    private readonly string _databaseName = DbContextFactory.NewDatabaseName();
    private readonly FlightDbContext _dbContext;
    private readonly FakeCommand _request = new("flight");

    public EfTxFlightBehaviorTests()
    {
        _dbContext = ModuleFakes.CreateFlightDbContext(_databaseName);
    }

    public void Dispose() => _dbContext.Dispose();

    private EfTxFlightBehavior<FakeCommand, string> CreateSut()
    {
        return new EfTxFlightBehavior<FakeCommand, string>(
            Substitute.For<ILogger<EfTxFlightBehavior<FakeCommand, string>>>(),
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
        _fixture.DispatcherTransactionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task handle_should_dispatch_flight_domain_events_and_persist_flight_inside_committed_transaction()
    {
        var flight = ModuleFakes.CreateFlight();

        var response = await CreateSut()
            .Handle(
                _request,
                TransactionBehaviorFixture.Handler("created", () => _dbContext.Flights.Add(flight)),
                CancellationToken.None
            );

        response.Should().Be("created");
        await _fixture
            .EventDispatcher.Received(1)
            .SendAsync(
                Arg.Is<IReadOnlyList<IDomainEvent>>(e =>
                    e.Count == 1 && ((FlightCreatedDomainEvent)e[0]).Id == flight.Id.Value
                ),
                typeof(FakeCommand),
                Arg.Any<CancellationToken>()
            );
        await _fixture.PersistMessageDbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        flight.DomainEvents.Should().BeEmpty();

        var transactionId = _fixture.DispatcherTransactionIds.Should().ContainSingle().Which;
        transactionId.Should().NotBeNull();
        _fixture.PersistSaveTransactionIds.Should().Equal(transactionId);
        _fixture.CompletedTransactions[transactionId!].Should().Be(TransactionStatus.Committed);

        await using var verification = ModuleFakes.CreateFlightDbContext(_databaseName);
        (await verification.Flights.SingleAsync()).Id.Should().Be(flight.Id);
    }

    [Fact]
    public async Task handle_should_abort_transaction_and_rethrow_when_outbox_save_fails()
    {
        _fixture.FailPersistMessageSave(new InvalidOperationException("outbox unavailable"));
        var flight = ModuleFakes.CreateFlight();

        var act = () =>
            CreateSut()
                .Handle(
                    _request,
                    TransactionBehaviorFixture.Handler("created", () => _dbContext.Flights.Add(flight)),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outbox unavailable");
        var transactionId = _fixture.PersistSaveTransactionIds.Should().ContainSingle().Which;
        _fixture.CompletedTransactions[transactionId!].Should().Be(TransactionStatus.Aborted);
        Transaction.Current.Should().BeNull();
    }

    [Fact]
    public async Task handle_should_rethrow_handler_failure_without_dispatching()
    {
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
        await _fixture.PersistMessageDbContext.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
