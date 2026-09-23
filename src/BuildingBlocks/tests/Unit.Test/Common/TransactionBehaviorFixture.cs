using System.Transactions;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Unit.Test.Common;

/// <summary>
/// Shared doubles for the EfTx*Behavior pipeline behaviors: a substituted event dispatcher (outbox) and
/// persist-message DbContext that record the ambient <see cref="Transaction"/> they were invoked under,
/// so tests can assert that outbox writes happen inside the same transaction as the module save and that
/// the transaction is aborted when any step fails.
/// </summary>
public sealed class TransactionBehaviorFixture
{
    private readonly HashSet<string> _observedTransactionIds = new();

    public TransactionBehaviorFixture()
    {
        BuildingBlocks.Polly.Extensions.Logger = Substitute.For<ILogger>();

        EventDispatcher
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ObserveAmbientTransaction(DispatcherTransactionIds);
                return Task.CompletedTask;
            });

        PersistMessageDbContext
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ObserveAmbientTransaction(PersistSaveTransactionIds);
                return Task.FromResult(1);
            });
    }

    public IEventDispatcher EventDispatcher { get; } = Substitute.For<IEventDispatcher>();

    public IPersistMessageDbContext PersistMessageDbContext { get; } = Substitute.For<IPersistMessageDbContext>();

    /// <summary>Ambient transaction id (null when none) seen by each call to the event dispatcher.</summary>
    public List<string?> DispatcherTransactionIds { get; } = new();

    /// <summary>Ambient transaction id (null when none) seen by each persist-message SaveChangesAsync call.</summary>
    public List<string?> PersistSaveTransactionIds { get; } = new();

    /// <summary>Final status of every ambient transaction observed through <see cref="ObserveAmbientTransaction"/>.</summary>
    public Dictionary<string, TransactionStatus> CompletedTransactions { get; } = new();

    public void ObserveAmbientTransaction(List<string?> sink)
    {
        var transaction = Transaction.Current;
        var id = transaction?.TransactionInformation.LocalIdentifier;
        sink.Add(id);

        if (transaction is not null && id is not null && _observedTransactionIds.Add(id))
        {
            transaction.TransactionCompleted += (_, e) =>
                CompletedTransactions[id] = e.Transaction!.TransactionInformation.Status;
        }
    }

    public void FailEventDispatcher(System.Exception exception)
    {
        EventDispatcher
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ObserveAmbientTransaction(DispatcherTransactionIds);
                return Task.FromException(exception);
            });
    }

    public void FailPersistMessageSave(System.Exception exception)
    {
        PersistMessageDbContext
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ObserveAmbientTransaction(PersistSaveTransactionIds);
                return Task.FromException<int>(exception);
            });
    }

    public static RequestHandlerDelegate<TResponse> Handler<TResponse>(TResponse response, Action? onInvoke = null)
    {
        return _ =>
        {
            onInvoke?.Invoke();
            return Task.FromResult(response);
        };
    }

    public static RequestHandlerDelegate<TResponse> FailingHandler<TResponse>(System.Exception exception)
    {
        return _ => Task.FromException<TResponse>(exception);
    }
}
