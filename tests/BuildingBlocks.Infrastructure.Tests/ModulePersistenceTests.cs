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

namespace BuildingBlocks.Infrastructure.Tests;

public class ModulePersistenceTests
{
    [Fact]
    public void ResolvesEachModuleAndPrefersItsExplicitConnectionString()
    {
        var configuration = Configuration(
            new()
            {
                ["PersistMessageOptions:ConnectionString:Flight"] = "flight",
                ["ConnectionStrings:flight-persist-message"] = "fallback",
                ["ConnectionStrings:passenger-persist-message"] = "passenger",
            }
        );
        Assert.Equal("flight", configuration.ResolvePersistMessageConnectionString("Flight"));
        Assert.Equal("passenger", configuration.ResolvePersistMessageConnectionString("Passenger"));
        Assert.Throws<ArgumentException>(() => configuration.ResolvePersistMessageConnectionString("Identity"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void BackgroundToggleDefaultsOnAndIsModuleSpecific(string? value, bool expected)
    {
        var configuration = Configuration(new() { ["Modules:Flight:BackgroundProcessingEnabled"] = value });
        Assert.Equal(expected, configuration.IsModuleBackgroundProcessingEnabled("Flight"));
        Assert.True(configuration.IsModuleBackgroundProcessingEnabled("Passenger"));
    }

    [Fact]
    public void ModuleDbContextsResolveConnectionStringsAfterRegistrationAndInheritTheCommonModel()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Modules:Flight:BackgroundProcessingEnabled"] = "false";
        builder.AddPersistMessageProcessor<TestModule>("Flight");
        builder.AddPersistMessageProcessor<OtherModule>("Passenger");
        builder.Configuration["PersistMessageOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight";
        builder.Configuration["PersistMessageOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger";
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var flight = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<TestModule>>();
        var passenger = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<OtherModule>>();
        Assert.Equal("Host=localhost;Database=flight", flight.Database.GetConnectionString());
        Assert.Equal("Host=localhost;Database=passenger", passenger.Database.GetConnectionString());
        Assert.IsAssignableFrom<PersistMessageDbContext>(flight);
        Assert.Equal("persist_message", flight.Model.FindEntityType(typeof(PersistMessage))!.GetTableName());
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IPersistMessageDbContext));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IPersistMessageProcessor));
        var workers = builder.Services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        Assert.DoesNotContain(
            workers,
            d => d.ImplementationType == typeof(PersistMessageBackgroundService<TestModule>)
        );
        Assert.Contains(workers, d => d.ImplementationType == typeof(PersistMessageBackgroundService<OtherModule>));
    }

    [Fact]
    public void MissingPersistenceConnectionStringFailsWhenContextIsResolved()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddPersistMessageProcessor<TestModule>("Missing");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Throws<ArgumentException>(
            () => scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<TestModule>>()
        );
    }

    [Fact]
    public void MongoOptionsAreNamedAndDoNotRegisterAnUnqualifiedContextOrRepository()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddMongoDbContext<ReadContext>("Flight");
        builder.AddMongoDbContext<OtherReadContext>("Passenger");
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:ConnectionString"] = "mongodb://localhost:27018",
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["MongoOptions:Passenger:DatabaseName"] = "passenger_service_read",
            }
        );
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<MongoOptions>>();
        Assert.Equal("mongodb://localhost:27018", options.Get("Flight").ConnectionString);
        Assert.Equal("mongodb://localhost:27017", options.Get("Passenger").ConnectionString);
        Assert.Equal("flight_service_read", options.Get("Flight").DatabaseName);
        Assert.Equal("passenger_service_read", options.Get("Passenger").DatabaseName);
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IMongoDbContext));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IMongoRepository<,>));
    }

    [Theory]
    [InlineData(null, "flight")]
    [InlineData("mongodb://localhost", null)]
    [InlineData("mongodb://localhost", "")]
    public void MongoRequiresBothConnectionStringAndModuleDatabase(string? connection, string? database)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["MongoOptions:ConnectionString"] = connection;
        builder.Configuration["MongoOptions:Flight:DatabaseName"] = database;
        builder.AddMongoDbContext<ReadContext>("Flight");
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Flight")
        );
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class OtherModule { }

    private sealed class ReadContext(IOptions<MongoOptions> options) : MongoDbContext(options);

    private sealed class OtherReadContext(IOptions<MongoOptions> options) : MongoDbContext(options);
}
