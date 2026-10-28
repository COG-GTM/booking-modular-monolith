using Booking.Extensions.Infrastructure;
using BuildingBlocks.EventStoreDB.BackgroundWorkers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Booking.Unit.Test;

public class BookingModuleSubscriptionRegistrationTests
{
    [Theory]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    [InlineData(null, 1)]
    public void SubscriptionWorkerRegistrationFollowsBackgroundProcessingSetting(
        string? backgroundProcessingEnabled,
        int expectedWorkerCount
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        var settings = new Dictionary<string, string?>
        {
            ["EventStoreOptions:ConnectionString"] = "esdb://127.0.0.1:1?tls=false",
            ["MongoOptions:ConnectionString"] = "mongodb://127.0.0.1:1",
            ["MongoOptions:Booking:DatabaseName"] = "booking_test",
            ["Grpc:FlightAddress"] = "http://127.0.0.1:1",
            ["Grpc:PassengerAddress"] = "http://127.0.0.1:1",
        };
        if (backgroundProcessingEnabled is not null)
            settings["Modules:Booking:BackgroundProcessingEnabled"] = backgroundProcessingEnabled;
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddBookingModules();

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var workers = serviceProvider.GetServices<IHostedService>().OfType<BackgroundWorker>();

        Assert.Equal(expectedWorkerCount, workers.Count());
    }
}
