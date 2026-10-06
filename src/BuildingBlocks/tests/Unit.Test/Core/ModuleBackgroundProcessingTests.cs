using BuildingBlocks.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Unit.Test.Core;

public class ModuleBackgroundProcessingTests
{
    [Fact]
    public void missing_module_setting_defaults_to_enabled()
    {
        var configuration = new ConfigurationBuilder().Build();

        configuration.IsModuleBackgroundProcessingEnabled("Booking").Should().BeTrue();
    }

    [Fact]
    public void disabling_one_module_does_not_disable_other_modules()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "false" }
            )
            .Build();

        configuration.IsModuleBackgroundProcessingEnabled("Booking").Should().BeFalse();
        configuration.IsModuleBackgroundProcessingEnabled("Flight").Should().BeTrue();
    }

    [Fact]
    public void explicit_true_keeps_module_background_processing_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "true" }
            )
            .Build();

        configuration.IsModuleBackgroundProcessingEnabled("Booking").Should().BeTrue();
    }

    [Fact]
    public void where_background_processing_enabled_filters_modules_and_preserves_order()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Modules:Identity:BackgroundProcessingEnabled"] = "false",
                    ["Modules:Booking:BackgroundProcessingEnabled"] = "false",
                }
            )
            .Build();
        (string ModuleName, string Item)[] modules =
        [
            ("Flight", "flight"),
            ("Identity", "identity"),
            ("Passenger", "passenger"),
            ("Booking", "booking"),
        ];

        configuration
            .WhereModuleBackgroundProcessingEnabled(modules)
            .Should()
            .Equal("flight", "passenger");
    }
}
