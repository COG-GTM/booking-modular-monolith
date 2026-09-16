using Mapster;
using MassTransit;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Dtos;
using Payments.FPS.OutboundPayments.Features.GettingPaymentById.V1;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;

namespace Payments.FPS.OutboundPayments.Features;

public class OutboundPaymentMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config
            .NewConfig<OutboundPayment, OutboundPaymentDto>()
            .ConstructUsing(x => new OutboundPaymentDto(
                x.Id,
                x.Amount.Value,
                x.Amount.Currency,
                x.DebtorAccount.SortCode,
                x.DebtorAccount.AccountNumber,
                x.CreditorAccount.SortCode,
                x.CreditorAccount.AccountNumber,
                x.Reference,
                x.Status,
                x.RejectionReason
            ));
        config
            .NewConfig<CreateOutboundPaymentMongo, OutboundPaymentReadModel>()
            .Map(d => d.Id, _ => NewId.NextGuid())
            .Map(d => d.OutboundPaymentId, s => s.Id);
        config
            .NewConfig<OutboundPayment, OutboundPaymentReadModel>()
            .Map(d => d.Id, _ => NewId.NextGuid())
            .Map(d => d.OutboundPaymentId, s => s.Id.Value)
            .Map(d => d.Amount, s => s.Amount.Value)
            .Map(d => d.Currency, s => s.Amount.Currency)
            .Map(d => d.DebtorSortCode, s => s.DebtorAccount.SortCode)
            .Map(d => d.DebtorAccountNumber, s => s.DebtorAccount.AccountNumber)
            .Map(d => d.CreditorSortCode, s => s.CreditorAccount.SortCode)
            .Map(d => d.CreditorAccountNumber, s => s.CreditorAccount.AccountNumber);
        config.NewConfig<OutboundPaymentReadModel, OutboundPaymentDto>().Map(d => d.Id, s => s.OutboundPaymentId);
        config
            .NewConfig<UpdateOutboundPaymentMongo, OutboundPaymentReadModel>()
            .Map(d => d.OutboundPaymentId, s => s.Id);
        config
            .NewConfig<SubmitPaymentRequestDto, SubmitPayment>()
            .ConstructUsing(x => new SubmitPayment(
                x.Amount,
                x.DebtorSortCode,
                x.DebtorAccountNumber,
                x.CreditorSortCode,
                x.CreditorAccountNumber,
                x.Reference
            ));
    }
}
