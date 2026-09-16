using BuildingBlocks.EFCore;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Payments.FPS.OutboundPayments.Models;

namespace Payments.FPS.Data.Seed;

public class PaymentsDataSeeder(PaymentsDbContext db, PaymentsReadDbContext readDb, IMapper mapper) : IDataSeeder
{
    public async Task SeedAllAsync()
    {
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
            return;

        if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.OutboundPayments))
        {
            await db.OutboundPayments.AddRangeAsync(InitialData.OutboundPayments);
            await db.SaveChangesAsync();
        }

        if (await readDb.OutboundPayment.EstimatedDocumentCountAsync() == 0)
            await readDb.OutboundPayment.InsertManyAsync(
                mapper.Map<List<OutboundPaymentReadModel>>(InitialData.OutboundPayments)
            );
    }
}
