using System.Reflection;
using BuildingBlocks.EFCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

// UseMigration<TContext> now seeds through ISeedManager.ExecuteSeedAsync(typeof(TContext).Assembly),
// so each module must run only the IDataSeeder implementations declared in its own assembly.
public class SeedManagerTests
{
    private sealed class LocalSeeder : IDataSeeder
    {
        public int Calls { get; private set; }

        public Task SeedAllAsync()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static (SeedManager Manager, LocalSeeder Local, IDataSeeder Foreign) CreateManager(string environmentName)
    {
        var local = new LocalSeeder();
        // NSubstitute proxies live in a dynamically generated assembly, so this seeder belongs to
        // a different assembly than LocalSeeder.
        var foreign = Substitute.For<IDataSeeder>();

        var services = new ServiceCollection();
        services.AddScoped<IDataSeeder>(_ => local);
        services.AddScoped<IDataSeeder>(_ => foreign);

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var manager = new SeedManager(NullLogger<SeedManager>.Instance, env, services.BuildServiceProvider());

        return (manager, local, foreign);
    }

    [Fact]
    public async Task execute_seed_for_an_assembly_runs_only_the_seeders_declared_in_that_assembly()
    {
        var (manager, local, foreign) = CreateManager("Development");

        await manager.ExecuteSeedAsync(typeof(LocalSeeder).Assembly);

        Assert.Equal(1, local.Calls);
        await foreign.DidNotReceive().SeedAllAsync();
    }

    [Fact]
    public async Task execute_seed_for_another_modules_assembly_skips_the_local_seeders()
    {
        var (manager, local, foreign) = CreateManager("Development");

        await manager.ExecuteSeedAsync(foreign.GetType().Assembly);

        Assert.Equal(0, local.Calls);
        await foreign.Received(1).SeedAllAsync();
    }

    [Fact]
    public async Task execute_seed_for_an_assembly_without_seeders_runs_nothing()
    {
        var (manager, local, foreign) = CreateManager("Development");

        await manager.ExecuteSeedAsync(typeof(SeedManager).Assembly);

        Assert.Equal(0, local.Calls);
        await foreign.DidNotReceive().SeedAllAsync();
    }

    [Fact]
    public async Task execute_seed_without_an_assembly_still_runs_every_seeder()
    {
        var (manager, local, foreign) = CreateManager("Development");

        await manager.ExecuteSeedAsync();

        Assert.Equal(1, local.Calls);
        await foreign.Received(1).SeedAllAsync();
    }

    [Fact]
    public async Task execute_seed_for_an_assembly_is_skipped_in_the_test_environment()
    {
        var (manager, local, foreign) = CreateManager("test");

        await manager.ExecuteSeedAsync(typeof(LocalSeeder).Assembly);

        Assert.Equal(0, local.Calls);
        await foreign.DidNotReceive().SeedAllAsync();
    }

    [Fact]
    public void seed_manager_interface_exposes_the_per_assembly_overload()
    {
        var method = typeof(ISeedManager).GetMethod(nameof(ISeedManager.ExecuteSeedAsync), [typeof(Assembly)]);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }
}
