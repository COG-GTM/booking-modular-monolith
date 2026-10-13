using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Messaging;

public record ServiceHostPing(Guid Id);

public class ServiceHostPingConsumer : IConsumer<ServiceHostPing>
{
    public Task Consume(ConsumeContext<ServiceHostPing> context) => Task.CompletedTask;
}

public class AddCustomMassTransitTests
{
    [Fact]
    public async Task configuration_overload_without_a_service_name_should_keep_the_default_queue_names()
    {
        await using var provider = BuildServiceHost(new Dictionary<string, string?>());

        var formatter = provider.GetRequiredService<IBusRegistrationContext>().EndpointNameFormatter;

        formatter.Should().BeOfType<DefaultEndpointNameFormatter>();
        formatter.Consumer<ServiceHostPingConsumer>().Should().Be("ServiceHostPing");
    }

    [Fact]
    public async Task configuration_overload_should_skip_the_inbox_when_it_is_disabled()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();

        await using var provider = BuildServiceHost(
            new Dictionary<string, string?> { ["MessageBrokerOptions:UseInbox"] = "false" },
            processor
        );

        await ConsumeOnePingAsync(provider);

        await processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task provided_assemblies_should_still_be_scanned_for_consumers()
    {
        await using var provider = BuildServiceHost(new Dictionary<string, string?>());

        provider.GetService<IConsumerTestHarness<ServiceHostPingConsumer>>().Should().NotBeNull();
    }

    private static async Task<ConsumeContext<ServiceHostPing>> ConsumeOnePingAsync(ServiceProvider provider)
    {
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        try
        {
            await harness.Bus.Publish(new ServiceHostPing(Guid.NewGuid()));

            var consumerHarness = harness.GetConsumerHarness<ServiceHostPingConsumer>();
            (await consumerHarness.Consumed.Any<ServiceHostPing>()).Should().BeTrue();

            return consumerHarness.Consumed.Select<ServiceHostPing>().Single().Context;
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static ServiceProvider BuildServiceHost(
        Dictionary<string, string?> settings,
        IPersistMessageProcessor? processor = null
    ) =>
        BuildHost(
            settings,
            processor ?? Substitute.For<IPersistMessageProcessor>(),
            (services, env) =>
            {
                var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
                services.AddCustomMassTransit(configuration, env, typeof(ServiceHostPingConsumer).Assembly);
            }
        );

    private static ServiceProvider BuildHost(
        Dictionary<string, string?> settings,
        IPersistMessageProcessor processor,
        Action<IServiceCollection, IWebHostEnvironment> register
    )
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("test");

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton(processor);

        register(services, env);

        return services.BuildServiceProvider(validateScopes: true);
    }
}
