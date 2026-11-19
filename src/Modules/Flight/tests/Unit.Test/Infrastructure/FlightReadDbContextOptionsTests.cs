using BuildingBlocks.Mongo;
using Flight.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Infrastructure;

public class FlightReadDbContextOptionsTests
{
    [Fact]
    public void flight_read_context_uses_flight_named_mongo_database()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["MongoOptions:Flight:DatabaseName"] = "flight_read";
        builder.Configuration["MongoOptions:Passenger:DatabaseName"] = "passenger_read";
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://localhost:27017";
        builder.AddMongoDbContext<FlightReadDbContext>("Flight");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<FlightReadDbContext>();

        Assert.Equal("flight_read", context.Database.DatabaseNamespace.DatabaseName);
    }
}
