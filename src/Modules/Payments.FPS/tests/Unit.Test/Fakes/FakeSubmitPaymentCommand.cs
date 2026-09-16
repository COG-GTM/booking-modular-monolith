using AutoBogus;
using MassTransit;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

namespace Unit.Test.Fakes;

public sealed class FakeSubmitPaymentCommand : AutoFaker<SubmitPayment>
{
    public FakeSubmitPaymentCommand()
    {
        RuleFor(x => x.Id, _ => NewId.NextGuid());
        RuleFor(x => x.Amount, _ => 125.50m);
        RuleFor(x => x.DebtorSortCode, _ => "040004");
        RuleFor(x => x.DebtorAccountNumber, _ => "12345678");
        RuleFor(x => x.CreditorSortCode, _ => "202020");
        RuleFor(x => x.CreditorAccountNumber, _ => "87654321");
        RuleFor(x => x.Reference, _ => "INV-1001");
    }
}
