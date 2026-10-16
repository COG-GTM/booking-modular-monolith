using System.ComponentModel.DataAnnotations;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class ConfigurationExtensionsTests
{
    public sealed class WebSectionOptions
    {
        public string Name { get; set; } = "default-name";
        public int Size { get; set; }
    }

    public sealed class WebValidatedOptions
    {
        [Required]
        public string? RequiredName { get; set; }
    }

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void get_options_from_configuration_binds_the_named_section()
    {
        var config = Config(new() { ["WebSection:Name"] = "bound", ["WebSection:Size"] = "7" });

        var model = config.GetOptions<WebSectionOptions>("WebSection");

        model.Name.Should().Be("bound");
        model.Size.Should().Be(7);
    }

    [Fact]
    public void get_options_from_configuration_returns_defaults_for_a_missing_section()
    {
        var model = Config(new()).GetOptions<WebSectionOptions>("Absent");

        model.Name.Should().Be("default-name");
        model.Size.Should().Be(0);
    }

    [Fact]
    public void get_options_from_services_reads_the_registered_configuration()
    {
        var config = Config(new() { ["WebSection:Name"] = "from-services" });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);

        services.GetOptions<WebSectionOptions>("WebSection").Name.Should().Be("from-services");
    }

    [Fact]
    public void get_options_from_web_application_reads_its_configuration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["WebSection:Name"] = "from-app" }
        );
        var app = builder.Build();

        app.GetOptions<WebSectionOptions>("WebSection").Name.Should().Be("from-app");
    }

    [Fact]
    public void add_validate_options_resolves_a_bound_singleton_when_valid()
    {
        var config = Config(new() { ["WebValidatedOptions:RequiredName"] = "present" });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddValidateOptions<WebValidatedOptions>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<WebValidatedOptions>().RequiredName.Should().Be("present");
    }

    [Fact]
    public void add_validate_options_throws_options_validation_when_required_value_is_missing()
    {
        var config = Config(new());
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddValidateOptions<WebValidatedOptions>();

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<WebValidatedOptions>();
        act.Should().Throw<OptionsValidationException>();
    }
}
