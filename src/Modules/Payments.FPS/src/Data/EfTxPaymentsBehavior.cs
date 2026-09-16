using System.Text.Json;
using System.Transactions;
using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Polly;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Payments.FPS.Data;

public class EfTxPaymentsBehavior<TRequest, TResponse>(
    ILogger<EfTxPaymentsBehavior<TRequest, TResponse>> logger,
    PaymentsDbContext db,
    IPersistMessageDbContext persist,
    IEventDispatcher dispatcher
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("{Prefix} Handled command {MediatrRequest}", GetType().Name, typeof(TRequest).FullName);
        logger.LogDebug(
            "{Prefix} Handled command {MediatrRequest} with content {RequestContent}",
            GetType().Name,
            typeof(TRequest).FullName,
            JsonSerializer.Serialize(request)
        );
        var response = await next();
        while (true)
        {
            var domainEvents = db.GetDomainEvents();
            if (domainEvents is null || !domainEvents.Any())
                return response;
            using var scope = new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                TransactionScopeAsyncFlowOption.Enabled
            );
            await dispatcher.SendAsync(domainEvents.ToArray(), typeof(TRequest), cancellationToken);
            await db.RetryOnFailure(async () => await db.SaveChangesAsync(cancellationToken));
            await persist.RetryOnFailure(async () => await persist.SaveChangesAsync(cancellationToken));
            scope.Complete();
            return response;
        }
    }
}
