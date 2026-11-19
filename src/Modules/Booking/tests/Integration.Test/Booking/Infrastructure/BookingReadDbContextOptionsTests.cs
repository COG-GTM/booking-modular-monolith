using Booking.Data;
using BuildingBlocks.Mongo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class BookingReadDbContextOptionsTests
{
    [Fact]
    public void booking_read_context_uses_booking_named_mongo_database()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["MongoOptions:Booking:DatabaseName"] = "booking_read";
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://localhost:27017";
        builder.AddMongoDbContext<BookingReadDbContext>("Booking");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<BookingReadDbContext>();

        Assert.Equal("booking_read", context.Database.DatabaseNamespace.DatabaseName);
    }
}
