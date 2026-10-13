using BuildingBlocks.EFCore;
using BuildingBlocks.Web;
using Flight.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Infrastructure;

public class ModuleConfigurationTests
{
    private static IConfiguration Build(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void background_processing_defaults_to_enabled_when_the_module_is_not_configured()
    {
        var configuration = Build(new());

        Assert.True(configuration.IsModuleBackgroundProcessingEnabled("Flight"));
        Assert.True(configuration.IsModuleBackgroundProcessingEnabled("Booking"));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    public void background_processing_reads_the_module_section(string value, bool expected)
    {
        var configuration = Build(new() { ["Modules:Passenger:BackgroundProcessingEnabled"] = value });

        Assert.Equal(expected, configuration.IsModuleBackgroundProcessingEnabled("Passenger"));
        Assert.True(configuration.IsModuleBackgroundProcessingEnabled("Flight"));
    }

    [Fact]
    public void background_processing_switch_is_scoped_to_the_named_module()
    {
        var configuration = Build(
            new()
            {
                ["Modules:Flight:BackgroundProcessingEnabled"] = "false",
                ["Modules:Identity:BackgroundProcessingEnabled"] = "true",
            }
        );

        Assert.False(configuration.IsModuleBackgroundProcessingEnabled("Flight"));
        Assert.True(configuration.IsModuleBackgroundProcessingEnabled("Identity"));
        Assert.True(configuration.IsModuleBackgroundProcessingEnabled("Passenger"));
    }

    [Fact]
    public void missing_write_db_connection_string_names_both_configuration_keys()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.AddCustomDbContext<FlightDbContext>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<ArgumentException>(
            () => scope.ServiceProvider.GetRequiredService<FlightDbContext>()
        );
        Assert.Contains("PostgresOptions:ConnectionString:Flight", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:flight", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void seed_manager_is_registered_once_across_many_db_contexts()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.AddCustomDbContext<FlightDbContext>("Flight");
        builder.AddCustomDbContext<FlightDbContext>("Flight");

        Assert.Single(builder.Services, d => d.ServiceType == typeof(ISeedManager));
    }
}
