using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.FPS.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        optionsBuilder.UseNpgsql(
            "Server=localhost;Port=5432;Database=payments;User Id=postgres;Password=postgres",
            x => x.MigrationsAssembly(typeof(PaymentsDbContext).Assembly.FullName)
        );
        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
