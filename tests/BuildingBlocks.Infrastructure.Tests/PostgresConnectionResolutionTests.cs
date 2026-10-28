using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class PostgresConnectionResolutionTests
{
    [Fact]
    public void Resolves_the_explicit_module_connection_string_without_an_Aspire_connection_string()
    {
        var configuration = Configuration(("PostgresOptions:ConnectionString:Flight", "Host=options;Database=flight"));

        Assert.Equal("Host=options;Database=flight", configuration.ResolvePostgresConnectionString("Flight"));
    }

    [Fact]
    public void Missing_module_connection_string_throws_with_the_module_name()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Configuration().ResolvePostgresConnectionString("Flight")
        );

        Assert.Contains("'Flight'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_modules_connection_string_does_not_resolve_flight()
    {
        var configuration = Configuration(
            ("PostgresOptions:ConnectionString:Passenger", "Host=options;Database=passenger"),
            ("ConnectionStrings:passenger", "Host=aspire;Database=passenger")
        );

        var exception = Assert.Throws<ArgumentException>(() => configuration.ResolvePostgresConnectionString("Flight"));

        Assert.Contains("'Flight'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Db_context_prefers_the_Aspire_connection_string_when_both_sources_are_configured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["ConnectionStrings:flight"] = "Host=aspire;Database=flight";
        builder.Configuration["PostgresOptions:ConnectionString:Flight"] = "Host=options;Database=flight";
        builder.AddCustomDbContext<ResolutionTestDbContext>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ResolutionTestDbContext>();

        Assert.Equal("Host=aspire;Database=flight", context.Database.GetConnectionString());
    }

    [Fact]
    public void Db_context_uses_the_explicit_connection_string_when_it_is_the_only_source()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["PostgresOptions:ConnectionString:Flight"] = "Host=options;Database=flight";
        builder.AddCustomDbContext<ResolutionTestDbContext>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ResolutionTestDbContext>();

        Assert.Equal("Host=options;Database=flight", context.Database.GetConnectionString());
    }

    [Fact]
    public void Db_context_reads_the_connection_string_from_configuration_at_resolution_time()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["PostgresOptions:ConnectionString:Flight"] = "Host=old;Database=flight";
        builder.AddCustomDbContext<ResolutionTestDbContext>("Flight");
        builder.Configuration["PostgresOptions:ConnectionString:Flight"] = "Host=new;Database=flight";

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ResolutionTestDbContext>();

        Assert.Equal("Host=new;Database=flight", context.Database.GetConnectionString());
    }

    [Fact]
    public void Db_context_resolution_throws_when_no_connection_string_is_configured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCustomDbContext<ResolutionTestDbContext>("Flight");

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<ArgumentException>(
            () => scope.ServiceProvider.GetRequiredService<ResolutionTestDbContext>()
        );

        Assert.Contains("'Flight'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Db_context_registration_tracks_only_non_empty_module_names()
    {
        var namedBuilder = WebApplication.CreateBuilder();
        namedBuilder.AddCustomDbContext<ResolutionTestDbContext>("Flight");
        var registration = Assert.Single(
            namedBuilder.Services,
            descriptor => descriptor.ServiceType == typeof(PostgresModuleRegistration)
        );
        Assert.Equal(new PostgresModuleRegistration("Flight"), registration.ImplementationInstance);

        var emptyNameBuilder = WebApplication.CreateBuilder();
        emptyNameBuilder.AddCustomDbContext<ResolutionTestDbContext>("");
        Assert.Empty(
            emptyNameBuilder.Services.Where(descriptor => descriptor.ServiceType == typeof(PostgresModuleRegistration))
        );

        var defaultNameBuilder = WebApplication.CreateBuilder();
        defaultNameBuilder.AddCustomDbContext<ResolutionTestDbContext>();
        Assert.Empty(
            defaultNameBuilder.Services.Where(descriptor =>
                descriptor.ServiceType == typeof(PostgresModuleRegistration)
            )
        );
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value))
            )
            .Build();

    private sealed class ResolutionTestDbContext(DbContextOptions<ResolutionTestDbContext> options)
        : DbContext(options),
            IDbContext
    {
        public IReadOnlyList<IDomainEvent> GetDomainEvents() => [];

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IExecutionStrategy CreateExecutionStrategy() => Database.CreateExecutionStrategy();

        public Task ExecuteTransactionalAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
