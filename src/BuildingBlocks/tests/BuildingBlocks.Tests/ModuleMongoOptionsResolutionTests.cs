using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Tests;

public class ModuleMongoOptionsResolutionTests
{
    [Fact]
    public async Task module_connection_uses_root_connection_string_as_last_fallback()
    {
        var builder = CreateBuilder();
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://root:27017";
        builder.Configuration["MongoOptions:First:DatabaseName"] = "first_read";
        builder.AddMongoDbContext<FirstRead>("First");
        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope
            .ServiceProvider.GetRequiredService<IOptionsSnapshot<MongoOptions>>()
            .Get("First")
            .ConnectionString.Should()
            .Be("mongodb://root:27017");
    }

    [Fact]
    public async Task module_without_any_connection_reports_module_name()
    {
        var builder = CreateBuilder();
        builder.Configuration["MongoOptions:First:DatabaseName"] = "first_read";
        builder.AddMongoDbContext<FirstRead>("First");
        await using var provider = builder.Services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("First");

        act.Should().Throw<OptionsValidationException>().WithMessage("*module 'First'*");
    }

    [Fact]
    public async Task named_module_configuration_does_not_leak_into_unnamed_options()
    {
        var builder = CreateBuilder();
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://root:27017";
        builder.Configuration["MongoOptions:First:DatabaseName"] = "first_read";
        builder.AddMongoDbContext<FirstRead>("First");
        await using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<MongoOptions>>().Value.DatabaseName.Should().BeNullOrEmpty();
    }

    private static WebApplicationBuilder CreateBuilder() =>
        WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );

    public sealed class First;

    public sealed class FirstRead(IOptionsSnapshot<MongoOptions> options)
        : MongoDbContext(Options.Create(options.Get("First")));
}
