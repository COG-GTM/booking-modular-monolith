using BuildingBlocks.EFCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class ModuleSeedingTests
{
    [Fact]
    public void Runs_only_seeders_from_the_contexts_assembly()
    {
        var (ownSeeder, foreignSeeder) = RunMigration("Development");

        Assert.NotEqual(typeof(SeedingContext).Assembly, foreignSeeder.GetType().Assembly);
        Assert.Equal(1, ownSeeder.Calls);
        foreignSeeder.DidNotReceive().SeedAllAsync();
    }

    [Fact]
    public void Skips_all_seeders_in_the_test_environment()
    {
        var (ownSeeder, foreignSeeder) = RunMigration("test");

        Assert.Equal(0, ownSeeder.Calls);
        foreignSeeder.DidNotReceive().SeedAllAsync();
    }

    private static (OwnModuleSeeder Own, IDataSeeder Foreign) RunMigration(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName = environmentName;
        services.AddSingleton(environment);

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<SeedingContext>(options => options.UseSqlite(connection));

        var ownSeeder = new OwnModuleSeeder();
        var foreignSeeder = Substitute.For<IDataSeeder>();
        foreignSeeder.SeedAllAsync().Returns(Task.CompletedTask);
        services.AddSingleton<IDataSeeder>(ownSeeder);
        services.AddSingleton<IDataSeeder>(foreignSeeder);

        using var provider = services.BuildServiceProvider();
        new ApplicationBuilder(provider).UseMigration<SeedingContext>();

        return (ownSeeder, foreignSeeder);
    }

    private sealed class OwnModuleSeeder : IDataSeeder
    {
        public int Calls { get; private set; }

        public Task SeedAllAsync()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class SeedingContext(DbContextOptions<SeedingContext> options) : DbContext(options), IDbContext
    {
        public IReadOnlyList<BuildingBlocks.Core.Event.IDomainEvent> GetDomainEvents() => [];

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
