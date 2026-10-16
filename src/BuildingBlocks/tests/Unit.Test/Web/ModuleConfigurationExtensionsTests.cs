using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class ModuleConfigurationExtensionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void missing_setting_defaults_to_enabled()
    {
        Config(new()).IsModuleBackgroundProcessingEnabled("Flight").Should().BeTrue();
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void explicit_setting_is_honoured(string value, bool expected)
    {
        Config(new() { ["Modules:Flight:BackgroundProcessingEnabled"] = value })
            .IsModuleBackgroundProcessingEnabled("Flight")
            .Should()
            .Be(expected);
    }

    [Fact]
    public void disabling_one_module_does_not_affect_another()
    {
        var config = Config(new() { ["Modules:Flight:BackgroundProcessingEnabled"] = "false" });

        config.IsModuleBackgroundProcessingEnabled("Passenger").Should().BeTrue();
    }
}
