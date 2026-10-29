using BuildingBlocks.PersistMessageProcessor;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Host.Test.Composition;

public sealed class BookingHostFactory : WebApplicationFactory<Booking.Host.Program>
{
    private readonly Dictionary<string, string?> _previousEnvironment = [];

    public BookingHostFactory()
    {
        SetEnvironmentVariable("MessagingOptions__Transport", "InMemory");
        SetEnvironmentVariable("Modules__Booking__BackgroundProcessingEnabled", "false");
        SetEnvironmentVariable(
            "PostgresOptions__ConnectionString__Booking",
            "Server=127.0.0.1;Port=1;Database=booking;Username=booking;Password=booking;Timeout=1"
        );
        SetEnvironmentVariable("MongoOptions__ConnectionString", "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=200");
        SetEnvironmentVariable("EventStoreOptions__ConnectionString", "esdb://127.0.0.1:2113?tls=false");
        SetEnvironmentVariable("Services__flight__grpc__0", "http://127.0.0.1:1");
        SetEnvironmentVariable("Services__passenger__grpc__0", "http://127.0.0.1:1");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("test");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPersistMessageDbContext<global::Booking.BookingRoot>>();
            services.AddScoped(_ => Substitute.For<IPersistMessageDbContext<global::Booking.BookingRoot>>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                foreach (var (key, value) in _previousEnvironment)
                    Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private void SetEnvironmentVariable(string key, string? value)
    {
        _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}
