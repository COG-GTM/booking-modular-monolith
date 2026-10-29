using BuildingBlocks.EFCore;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Tests;

public class CustomDbContextLazyConnectionTests
{
    [Fact]
    public void postgres_connection_is_resolved_after_context_registration()
    {
        var builder = CreateBuilder();
        builder.AddCustomDbContext<TestDbContext>("First");
        var connectionString = Connection("first");
        builder.Configuration["PostgresOptions:ConnectionString:First"] = connectionString;

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<TestDbContext>()
            .Database.GetConnectionString()
            .Should()
            .Be(connectionString);
    }

    [Fact]
    public void aspire_connection_precedes_postgres_module_connection()
    {
        var builder = CreateBuilder();
        builder.Configuration["ConnectionStrings:first"] = Connection("aspire");
        builder.Configuration["PostgresOptions:ConnectionString:First"] = Connection("postgres");
        builder.AddCustomDbContext<TestDbContext>("First");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<TestDbContext>()
            .Database.GetConnectionString()
            .Should()
            .Be(Connection("aspire"));
    }

    [Fact]
    public void missing_connection_reports_module_name()
    {
        var builder = CreateBuilder();
        builder.AddCustomDbContext<TestDbContext>("First");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<TestDbContext>();

        act.Should().Throw<ArgumentException>().WithMessage("*'First'*");
    }

    private static WebApplicationBuilder CreateBuilder() =>
        WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );

    private static string Connection(string database) =>
        $"Host=localhost;Database={database};Username=test;Password=test";

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : AppDbContextBase(options);
}
