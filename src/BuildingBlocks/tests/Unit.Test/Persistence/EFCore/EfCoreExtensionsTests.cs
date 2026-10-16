using BuildingBlocks.EFCore;
using BuildingBlocks.Unit.Test.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class EfCoreExtensionsTests
{
    private sealed class SnakeCaseParent : BuildingBlocks.Core.Model.IEntity
    {
        public int ParentId { get; set; }
        public List<SnakeCaseChild> Children { get; } = [];
        public DateTime? CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime? LastModified { get; set; }
        public long? LastModifiedBy { get; set; }
        public bool IsDeleted { get; set; }
        public long Version { get; set; }
    }

    private sealed class SnakeCaseChild
    {
        public int ChildId { get; set; }
        public string ChildName { get; set; } = string.Empty;
        public SnakeCaseParent? Parent { get; set; }
    }

    private static WebApplicationBuilder CreateBuilder(IEnumerable<KeyValuePair<string, string?>>? settings = null)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        if (settings is not null)
            builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    [Fact]
    public async Task to_snake_case_tables_underscores_table_column_key_and_fk_names()
    {
        var options = new DbContextOptionsBuilder<PersistSnakeCaseDbContext>().UseSqlite("DataSource=:memory:").Options;
        await using var context = new PersistSnakeCaseDbContext(options);
        var model = context.Model;

        var parent = model.FindEntityType(typeof(PersistSnakeParent))!;
        var child = model.FindEntityType(typeof(PersistSnakeChild))!;
        var table = StoreObjectIdentifier.Table(child.GetTableName()!, child.GetSchema());

        parent.GetTableName().Should().Be("persist_snake_parent");
        child.GetTableName().Should().Be("persist_snake_child");
        child.FindProperty(nameof(PersistSnakeChild.ChildName))!.GetColumnName(table).Should().Be("child_name");
        child.GetKeys().Single().GetName().Should().Be("pk_persist_snake_child");
        parent.GetKeys().Single().GetName().Should().Be("pk_persist_snake_parent");
        child
            .GetForeignKeys()
            .Single()
            .GetConstraintName()
            .Should()
            .Be("fk_persist_snake_child_persist_snake_parent_parent_id");
    }

    [Fact]
    public void filter_soft_deleted_properties_sets_query_filter_only_on_entities()
    {
        var builder = new ModelBuilder();
        builder.Entity<PersistFlightAggregate>();
        builder.Entity<SnakeCaseChild>();

        builder.FilterSoftDeletedProperties();

        builder.Model.FindEntityType(typeof(PersistFlightAggregate))!.GetQueryFilter().Should().NotBeNull();
        builder.Model.FindEntityType(typeof(SnakeCaseChild))!.GetQueryFilter().Should().BeNull();
    }

    [Fact]
    public async Task add_custom_db_context_prefers_connection_strings_over_postgres_options()
    {
        var builder = CreateBuilder(
            [
                new("ConnectionStrings:flight-schedule", "Host=aspire;Database=aspire_db"),
                new("PostgresOptions:ConnectionString:FlightSchedule", "Host=options;Database=options_db"),
            ]
        );

        builder.AddCustomDbContext<PersistAppDbContext>("FlightSchedule");

        await using var scope = builder.Services.BuildServiceProvider().CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PersistAppDbContext>();
        context.Database.GetConnectionString().Should().Contain("Host=aspire");
    }

    [Fact]
    public async Task add_custom_db_context_falls_back_to_postgres_options()
    {
        var builder = CreateBuilder(
            [new("PostgresOptions:ConnectionString:FlightSchedule", "Host=options;Database=options_db")]
        );

        builder.AddCustomDbContext<PersistAppDbContext>("FlightSchedule");

        await using var scope = builder.Services.BuildServiceProvider().CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PersistAppDbContext>();
        context.Database.GetConnectionString().Should().Contain("Host=options");
    }

    [Fact]
    public async Task add_custom_db_context_throws_argument_exception_when_connection_string_missing()
    {
        var builder = CreateBuilder();

        builder.AddCustomDbContext<PersistAppDbContext>("FlightSchedule");

        await using var scope = builder.Services.BuildServiceProvider().CreateAsyncScope();
        var act = () => scope.ServiceProvider.GetRequiredService<PersistAppDbContext>();

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*PostgresOptions:ConnectionString:FlightSchedule*ConnectionStrings:flight-schedule*");
    }

    [Fact]
    public void add_custom_db_context_registers_seed_manager_and_idb_context()
    {
        var builder = CreateBuilder([new("ConnectionStrings:flight-schedule", "Host=x;Database=y")]);

        builder.AddCustomDbContext<PersistAppDbContext>("FlightSchedule");

        builder.Services.Should().Contain(d => d.ServiceType == typeof(ISeedManager));
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IDbContext));
    }
}
