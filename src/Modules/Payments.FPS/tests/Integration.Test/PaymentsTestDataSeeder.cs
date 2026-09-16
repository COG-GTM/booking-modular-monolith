using BuildingBlocks.EFCore;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Payments.FPS.Data;
using Payments.FPS.Data.Seed;
using Payments.FPS.OutboundPayments.Models;

namespace Integration.Test;

public class PaymentsTestDataSeeder(PaymentsDbContext db, PaymentsReadDbContext readDb, IMapper mapper)
    : ITestDataSeeder
{
    public async Task SeedAllAsync()
    {
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(db.OutboundPayments))
        {
            await db.OutboundPayments.AddRangeAsync(InitialData.OutboundPayments);
            await db.SaveChangesAsync();
        }

        if (await readDb.OutboundPayment.EstimatedDocumentCountAsync() == 0)
        {
            await readDb.OutboundPayment.InsertManyAsync(
                mapper.Map<List<OutboundPaymentReadModel>>(InitialData.OutboundPayments)
            );
        }
    }
}
