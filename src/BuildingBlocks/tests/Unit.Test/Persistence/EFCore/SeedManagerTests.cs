using BuildingBlocks.EFCore;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class SeedManagerTests
{
    private static IWebHostEnvironment Environment(string name)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static ServiceProvider Provider(IServiceCollection services) => services.BuildServiceProvider();

    [Fact]
    public async Task execute_seed_async_with_assembly_runs_only_seeders_from_that_assembly()
    {
        var localSeeder = new PersistTestSeeder();
        var proxySeeder = Substitute.For<IDataSeeder>();
        var services = new ServiceCollection().AddSingleton<IDataSeeder>(localSeeder).AddSingleton(proxySeeder);
        var provider = Provider(services);
        var seedManager = new SeedManager(NullLogger<SeedManager>.Instance, Environment("Development"), provider);

        await seedManager.ExecuteSeedAsync(typeof(SeedManagerTests).Assembly);

        localSeeder.Calls.Should().Be(1);
        // NSubstitute proxies live in a dynamic assembly, so they are not module seeders.
        await proxySeeder.DidNotReceive().SeedAllAsync();
    }

    [Fact]
    public async Task execute_seed_async_without_assembly_runs_all_seeders()
    {
        var localSeeder = new PersistTestSeeder();
        var proxySeeder = Substitute.For<IDataSeeder>();
        var services = new ServiceCollection().AddSingleton<IDataSeeder>(localSeeder).AddSingleton(proxySeeder);
        var provider = Provider(services);
        var seedManager = new SeedManager(NullLogger<SeedManager>.Instance, Environment("Development"), provider);

        await seedManager.ExecuteSeedAsync();

        localSeeder.Calls.Should().Be(1);
        await proxySeeder.Received(1).SeedAllAsync();
    }

    [Fact]
    public async Task execute_seed_async_runs_nothing_in_test_environment()
    {
        var localSeeder = new PersistTestSeeder();
        var services = new ServiceCollection().AddSingleton<IDataSeeder>(localSeeder);
        var provider = Provider(services);
        var seedManager = new SeedManager(NullLogger<SeedManager>.Instance, Environment("test"), provider);

        await seedManager.ExecuteSeedAsync();

        localSeeder.Calls.Should().Be(0);
    }

    [Fact]
    public async Task execute_test_seed_async_runs_test_data_seeders_even_in_test_environment()
    {
        var testSeeder = new PersistTestDataSeeder();
        var services = new ServiceCollection().AddSingleton<ITestDataSeeder>(testSeeder);
        var provider = Provider(services);
        var seedManager = new SeedManager(NullLogger<SeedManager>.Instance, Environment("test"), provider);

        await seedManager.ExecuteTestSeedAsync();

        testSeeder.Calls.Should().Be(1);
    }
}
