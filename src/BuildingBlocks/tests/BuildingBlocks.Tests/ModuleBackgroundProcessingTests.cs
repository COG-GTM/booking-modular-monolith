using System.Reflection;
using BuildingBlocks.MassTransit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Tests;

public class ModuleBackgroundProcessingTests
{
    [Fact]
    public void module_background_processing_defaults_to_enabled()
    {
        var configuration = new ConfigurationBuilder().Build();

        ModuleBackgroundProcessing.IsEnabled(configuration, "Passenger").Should().BeTrue();
    }

    [Fact]
    public void disabled_module_consumers_are_excluded()
    {
        var configuration = Configuration(("Modules:Passenger:BackgroundProcessingEnabled", "false"));
        var passengerAssembly = typeof(string).Assembly;
        var otherAssembly = typeof(ConfigurationBuilder).Assembly;

        var assemblies = ModuleBackgroundProcessing.ConsumerAssemblies(
            configuration,
            ("Passenger", passengerAssembly),
            ("Identity", otherAssembly)
        );

        assemblies.Should().Equal(otherAssembly);
    }

    [Fact]
    public void missing_module_setting_keeps_consumer_enabled()
    {
        var configuration = Configuration(("Modules:Passenger:BackgroundProcessingEnabled", "true"));

        ModuleBackgroundProcessing
            .ConsumerAssemblies(configuration, ("Flight", typeof(Assembly).Assembly))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void false_setting_is_case_insensitive()
    {
        var configuration = Configuration(("Modules:Passenger:BackgroundProcessingEnabled", "False"));

        ModuleBackgroundProcessing.IsEnabled(configuration, "Passenger").Should().BeFalse();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}
