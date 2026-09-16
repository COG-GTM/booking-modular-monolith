namespace Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

using Ardalis.GuardClauses;
using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Core.Event;
using MapsterMapper;
using MassTransit;
using MediatR;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Models;

public record CreateOutboundPaymentMongo(
    Guid Id,
    decimal Amount,
    string Currency,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference,
    PaymentStatus Status,
    bool IsDeleted = false
) : InternalCommand;

internal class CreateOutboundPaymentMongoHandler(PaymentsReadDbContext db, IMapper mapper)
    : ICommandHandler<CreateOutboundPaymentMongo>
{
    public async Task<Unit> Handle(CreateOutboundPaymentMongo request, CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(request));
        var readModel = mapper.Map<OutboundPaymentReadModel>(request);
        if (
            await db
                .OutboundPayment.AsQueryable()
                .AnyAsync(x => x.OutboundPaymentId == request.Id && !x.IsDeleted, cancellationToken)
        )
            throw new OutboundPaymentAlreadyExistException();
        await db.OutboundPayment.InsertOneAsync(readModel, cancellationToken: cancellationToken);
        return Unit.Value;
    }
}
