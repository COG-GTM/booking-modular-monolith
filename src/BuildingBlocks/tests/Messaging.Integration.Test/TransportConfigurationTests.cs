using BuildingBlocks.MassTransit;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class TransportConfigurationTests
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public void Rejects_unsafe_in_memory_fallback(string environment, bool allow)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Messaging:Transport"] = "InMemory",
                    ["Messaging:AllowInMemory"] = allow.ToString(),
                }
            )
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddCustomMassTransit(env, config, "passenger")
        );
    }

    [Theory]
    [InlineData("test", false)]
    [InlineData("Development", true)]
    public async Task Allows_explicit_dev_or_test_transport_without_discovering_loaded_consumers(
        string environment,
        bool allow
    )
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Messaging:Transport"] = "InMemory",
                    ["Messaging:AllowInMemory"] = allow.ToString(),
                }
            )
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddCustomMassTransit(env, config, "publisher-only");
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        await bus.StopAsync();
    }
}
