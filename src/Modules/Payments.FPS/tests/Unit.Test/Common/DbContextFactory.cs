using Microsoft.EntityFrameworkCore;
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;

namespace Unit.Test.Common;

public static class DbContextFactory
{
    public static PaymentsDbContext Create()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new PaymentsDbContext(options);
        context.OutboundPayments.Add(
            Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
                OutboundPaymentId.Of(Guid.NewGuid()),
                Amount.Of(100m),
                UkAccount.Of("040004", "12345678"),
                UkAccount.Of("202020", "87654321"),
                "SEED"
            )
        );
        context.SaveChanges();
        return context;
    }

    public static void Destroy(PaymentsDbContext context)
    {
        context.Database.EnsureDeleted();
        context.Dispose();
    }
}
