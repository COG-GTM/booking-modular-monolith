using BuildingBlocks.Core;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Infrastructure;

public class PerModuleDataStoreTests
{
    private const string FlightPersist =
        "Server=localhost;Port=5432;Database=flight_service_persist_message;User Id=flight_service;Password=x";
    private const string PassengerPersist =
        "Server=localhost;Port=5432;Database=passenger_service_persist_message;User Id=passenger_service;Password=x";

    public sealed class FlightModule;

    public sealed class PassengerModule;

    public sealed class FlightRead(IOptions<MongoOptions> options) : MongoDbContext(options);

    public sealed class PassengerRead(IOptions<MongoOptions> options) : MongoDbContext(options);

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static Dictionary<string, string?> PersistSettings() =>
        new()
        {
            ["PersistMessageOptions:ConnectionString:Flight"] = FlightPersist,
            ["PersistMessageOptions:ConnectionString:Passenger"] = PassengerPersist,
        };

    private static int CountWorkers<TModule>(IServiceCollection services)
        where TModule : class =>
        services.Count(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(PersistMessageBackgroundService<TModule>)
        );

    [Fact]
    public void each_module_gets_its_own_persist_message_store()
    {
        var builder = CreateBuilder(PersistSettings());
        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var flight = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<FlightModule>>();
        var passenger = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<PassengerModule>>();

        Assert.Contains(
            "flight_service_persist_message",
            flight.Database.GetConnectionString(),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "passenger_service_persist_message",
            passenger.Database.GetConnectionString(),
            StringComparison.Ordinal
        );
        Assert.NotSame(flight, passenger);

        Assert.Equal(
            new[] { "Flight", "Passenger" },
            provider.GetServices<PersistMessageStore>().Select(s => s.ModuleName).ToArray()
        );
        Assert.Contains(
            builder.Services,
            d =>
                d.ServiceType == typeof(IEventDispatcher<FlightModule>)
                && d.ImplementationType == typeof(EventDispatcher<FlightModule>)
        );
        Assert.Equal(1, CountWorkers<FlightModule>(builder.Services));
        Assert.Equal(1, CountWorkers<PassengerModule>(builder.Services));
    }

    [Fact]
    public void aspire_connection_string_takes_precedence()
    {
        var settings = PersistSettings();
        settings["ConnectionStrings:flight-persist-message"] =
            "Server=aspire;Port=5432;Database=flight_from_aspire;User Id=u;Password=p";

        var builder = CreateBuilder(settings);
        builder.AddPersistMessageProcessor<FlightModule>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var flight = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<FlightModule>>();
        Assert.Contains("flight_from_aspire", flight.Database.GetConnectionString(), StringComparison.Ordinal);
    }

    [Fact]
    public void missing_module_connection_string_fails_instead_of_falling_back_to_a_shared_store()
    {
        var builder = CreateBuilder(new() { ["PersistMessageOptions:ConnectionString"] = FlightPersist });
        builder.AddPersistMessageProcessor<FlightModule>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<ArgumentException>(
            () => scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<FlightModule>>()
        );
        Assert.Contains("PersistMessageOptions:ConnectionString:Flight", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void background_processing_switch_disables_only_that_modules_worker()
    {
        var settings = PersistSettings();
        settings["Modules:Passenger:BackgroundProcessingEnabled"] = "false";

        var builder = CreateBuilder(settings);
        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");

        Assert.Equal(1, CountWorkers<FlightModule>(builder.Services));
        Assert.Equal(0, CountWorkers<PassengerModule>(builder.Services));
        Assert.True(builder.Configuration.IsModuleBackgroundProcessingEnabled("Flight"));
        Assert.False(builder.Configuration.IsModuleBackgroundProcessingEnabled("Passenger"));
    }

    [Fact]
    public void persist_message_enabled_false_disables_all_workers()
    {
        var settings = PersistSettings();
        settings["PersistMessageOptions:Enabled"] = "false";

        var builder = CreateBuilder(settings);
        builder.AddPersistMessageProcessor<FlightModule>("Flight");

        Assert.Equal(0, CountWorkers<FlightModule>(builder.Services));
    }

    [Fact]
    public void each_module_reads_from_its_own_mongo_database()
    {
        var builder = CreateBuilder(
            new()
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["MongoOptions:Passenger:DatabaseName"] = "passenger_service_read",
                ["MongoOptions:Passenger:ConnectionString"] = "mongodb://passenger-mongo:27017",
            }
        );
        builder.AddMongoDbContext<FlightRead>("Flight");
        builder.AddMongoDbContext<PassengerRead>("Passenger");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var flight = scope.ServiceProvider.GetRequiredService<FlightRead>();
        var passenger = scope.ServiceProvider.GetRequiredService<PassengerRead>();

        Assert.Equal("flight_service_read", flight.Database.DatabaseNamespace.DatabaseName);
        Assert.Equal("passenger_service_read", passenger.Database.DatabaseNamespace.DatabaseName);
        Assert.Equal("localhost", flight.MongoClient.Settings.Server.Host);
        Assert.Equal("passenger-mongo", passenger.MongoClient.Settings.Server.Host);

        // An unqualified context/repository would silently bind to the last registered module.
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IMongoDbContext));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IMongoRepository<,>));
        Assert.Same(flight, scope.ServiceProvider.GetRequiredService<IMongoUnitOfWork<FlightRead>>().Context);
    }

    [Fact]
    public void mongo_module_without_database_name_fails_instead_of_sharing_one()
    {
        var builder = CreateBuilder(
            new()
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:DatabaseName"] = "booking_modular_monolith_read",
            }
        );
        builder.AddMongoDbContext<FlightRead>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<FlightRead>());
    }
}
