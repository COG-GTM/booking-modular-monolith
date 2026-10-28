using BuildingBlocks.Mongo;
using Flight.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Common;

public class FlightReadDbContextTests
{
    [Fact]
    public void Uses_the_flight_named_mongo_options()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://localhost:27017";
        builder.Configuration["MongoOptions:Flight:DatabaseName"] = "flight_service_read";
        builder.Configuration["MongoOptions:Passenger:DatabaseName"] = "passenger_service_read";
        builder.AddMongoDbContext<FlightReadDbContext>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FlightReadDbContext>();

        Assert.Equal("flight_service_read", context.Database.DatabaseNamespace.DatabaseName);
        Assert.Equal("flight_service_read", context.Flight.Database.DatabaseNamespace.DatabaseName);
        Assert.Null(scope.ServiceProvider.GetService<IMongoDbContext>());
    }
}
