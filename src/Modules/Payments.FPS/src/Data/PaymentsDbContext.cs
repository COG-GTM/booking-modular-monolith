using System.Reflection;
using BuildingBlocks.EFCore;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payments.FPS.OutboundPayments.Models;

namespace Payments.FPS.Data;

public sealed class PaymentsDbContext(
    DbContextOptions<PaymentsDbContext> options,
    ICurrentUserProvider? currentUserProvider = null,
    ILogger<PaymentsDbContext>? logger = null
) : AppDbContextBase(options, currentUserProvider, logger)
{
    public DbSet<OutboundPayment> OutboundPayments => Set<OutboundPayment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.FilterSoftDeletedProperties();
        builder.ToSnakeCaseTables();
    }
}
