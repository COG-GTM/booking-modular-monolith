using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

namespace EndToEnd.Test.Fakes;

public static class FakeSubmitPaymentCommand
{
    public static SubmitPaymentRequestDto Create() =>
        new(125.50m, "040004", "12345678", "202020", "87654321", "INV-1001");
}
