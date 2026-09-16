using AutoBogus;
using MassTransit;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

namespace Integration.Test.Fakes;

public sealed class FakeCreateOutboundPaymentMongoCommand : AutoFaker<CreateOutboundPaymentMongo>
{
    public FakeCreateOutboundPaymentMongoCommand()
    {
        RuleFor(x => x.Id, _ => NewId.NextGuid());
        RuleFor(x => x.Amount, _ => 125.50m);
        RuleFor(x => x.Currency, _ => "GBP");
        RuleFor(x => x.DebtorSortCode, _ => "040004");
        RuleFor(x => x.DebtorAccountNumber, _ => "12345678");
        RuleFor(x => x.CreditorSortCode, _ => "202020");
        RuleFor(x => x.CreditorAccountNumber, _ => "87654321");
        RuleFor(x => x.Reference, _ => "INV-1001");
        RuleFor(x => x.Status, _ => PaymentStatus.Submitted);
        RuleFor(x => x.IsDeleted, _ => false);
    }
}
