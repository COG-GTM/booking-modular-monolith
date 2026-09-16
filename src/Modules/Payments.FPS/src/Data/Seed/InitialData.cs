using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;

namespace Payments.FPS.Data.Seed;

public static class InitialData
{
    public static List<OutboundPayment> OutboundPayments { get; } =
        new()
        {
            OutboundPayment.Create(
                OutboundPaymentId.Of(new Guid("3c5c0000-97c6-fc34-a0cb-08db322230c8")),
                Amount.Of(125.50m),
                UkAccount.Of("040004", "12345678"),
                UkAccount.Of("202020", "87654321"),
                "INV-1001"
            ),
        };
}
