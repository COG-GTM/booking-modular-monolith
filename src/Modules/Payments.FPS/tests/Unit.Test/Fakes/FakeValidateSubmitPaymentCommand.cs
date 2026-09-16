using AutoBogus;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

namespace Unit.Test.Fakes;

public sealed class FakeValidateSubmitPaymentCommand : AutoFaker<SubmitPayment>
{
    public FakeValidateSubmitPaymentCommand()
    {
        RuleFor(x => x.Amount, _ => 0m);
        RuleFor(x => x.DebtorSortCode, _ => "12");
        RuleFor(x => x.DebtorAccountNumber, _ => "abc");
        RuleFor(x => x.CreditorSortCode, _ => "12");
        RuleFor(x => x.CreditorAccountNumber, _ => "abc");
    }
}
