namespace Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;

using Ardalis.GuardClauses;
using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Core.Event;
using MediatR;
using MongoDB.Driver;
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Models;

public record UpdateOutboundPaymentMongo(Guid Id, PaymentStatus Status, string? RejectionReason, bool IsDeleted = false)
    : InternalCommand;

internal class UpdateOutboundPaymentMongoHandler(PaymentsReadDbContext db) : ICommandHandler<UpdateOutboundPaymentMongo>
{
    public async Task<Unit> Handle(UpdateOutboundPaymentMongo request, CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(request));
        var filter = Builders<OutboundPaymentReadModel>.Filter.Eq(x => x.OutboundPaymentId, request.Id);
        if (await db.OutboundPayment.Find(filter).FirstOrDefaultAsync(cancellationToken) is null)
            throw new OutboundPaymentNotFoundException();
        await db.OutboundPayment.UpdateOneAsync(
            filter,
            Builders<OutboundPaymentReadModel>
                .Update.Set(x => x.Status, request.Status)
                .Set(x => x.RejectionReason, request.RejectionReason)
                .Set(x => x.IsDeleted, request.IsDeleted),
            cancellationToken: cancellationToken
        );
        return Unit.Value;
    }
}
