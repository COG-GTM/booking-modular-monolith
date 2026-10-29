using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace BuildingBlocks.Tests;

public class ServiceHostSettingsResolutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task service_host_settings_resolve_per_module_data_options(bool dockerSettings)
    {
        var builder = CreateBuilder(dockerSettings);
        builder.AddPersistMessageProcessor<IdentityModule>("Identity");
        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");
        builder.AddPersistMessageProcessor<BookingModule>("Booking");
        builder.AddMongoDbContext<FlightRead>("Flight");
        builder.AddMongoDbContext<PassengerRead>("Passenger");
        builder.AddMongoDbContext<BookingRead>("Booking");
        var hostedServiceTypes = builder
            .Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        hostedServiceTypes.Should().Contain(typeof(PersistMessageBackgroundService<IdentityModule>));
        hostedServiceTypes.Should().Contain(typeof(PersistMessageBackgroundService<FlightModule>));
        hostedServiceTypes.Should().Contain(typeof(PersistMessageBackgroundService<PassengerModule>));
        hostedServiceTypes.Should().Contain(typeof(PersistMessageBackgroundService<BookingModule>));
        builder.Configuration["PersistMessageOptions:ConnectionString"].Should().BeNull();

        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        AssertPersistMessageConnection<IdentityModule>(services, "Identity", dockerSettings);
        AssertPersistMessageConnection<FlightModule>(services, "Flight", dockerSettings);
        AssertPersistMessageConnection<PassengerModule>(services, "Passenger", dockerSettings);
        AssertPersistMessageConnection<BookingModule>(services, "Booking", dockerSettings);
        AssertMongoOptions(services, "Flight", dockerSettings);
        AssertMongoOptions(services, "Passenger", dockerSettings);
        AssertMongoOptions(services, "Booking", dockerSettings);
    }

    private static WebApplicationBuilder CreateBuilder(bool dockerSettings)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration.Sources.Clear();
        var servicesDirectory = Path.Combine(FindRepositoryRoot(), "src", "Services");
        foreach (var module in new[] { "Identity", "Flight", "Passenger", "Booking" })
        {
            var hostSettings = Path.Combine(servicesDirectory, $"{module}.Host");
            builder.Configuration.AddJsonFile(Path.Combine(hostSettings, "appsettings.json"), optional: false);
            if (dockerSettings)
                builder.Configuration.AddJsonFile(
                    Path.Combine(hostSettings, "appsettings.docker.json"),
                    optional: false
                );
        }
        return builder;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate booking-modular-monolith.sln.");
    }

    private static void AssertPersistMessageConnection<TModule>(
        IServiceProvider services,
        string moduleName,
        bool dockerSettings
    )
    {
        var context = services.GetRequiredService<PersistMessageDbContext<TModule>>();
        var connection = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        connection.Database.Should().Be($"{moduleName.ToLowerInvariant()}_service");
        if (dockerSettings)
            connection.Username.Should().Be($"{moduleName.ToLowerInvariant()}_service");
    }

    private static void AssertMongoOptions(IServiceProvider services, string moduleName, bool dockerSettings)
    {
        var options = services.GetRequiredService<IOptionsSnapshot<MongoOptions>>().Get(moduleName);
        options.DatabaseName.Should().Be($"{moduleName.ToLowerInvariant()}_service_read");
        options.ConnectionString.Should().Be(dockerSettings ? "mongodb://mongo:27017" : "mongodb://localhost:27017");
    }

    public sealed class IdentityModule;

    public sealed class FlightModule;

    public sealed class PassengerModule;

    public sealed class BookingModule;

    public sealed class FlightRead(IOptionsSnapshot<MongoOptions> options)
        : MongoDbContext(Options.Create(options.Get("Flight")));

    public sealed class PassengerRead(IOptionsSnapshot<MongoOptions> options)
        : MongoDbContext(Options.Create(options.Get("Passenger")));

    public sealed class BookingRead(IOptionsSnapshot<MongoOptions> options)
        : MongoDbContext(Options.Create(options.Get("Booking")));
}
