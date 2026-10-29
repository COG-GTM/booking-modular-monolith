using BuildingBlocks.EFCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class SeedManagerEnvironmentTests
{
    [Fact]
    public async Task test_environment_skips_regular_seeders()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("test");
        var seeder = new TestSeeder();
        var services = new ServiceCollection();
        services.AddSingleton<IDataSeeder>(seeder);
        await using var provider = services.BuildServiceProvider();
        var manager = new SeedManager(NullLogger<SeedManager>.Instance, environment, provider);

        await manager.ExecuteSeedAsync(typeof(SeedManagerEnvironmentTests).Assembly);

        Assert.Equal(0, seeder.Calls);
    }

    private sealed class TestSeeder : IDataSeeder
    {
        public int Calls { get; private set; }

        public Task SeedAllAsync()
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
