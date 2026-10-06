using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Unit.Test.PersistMessageProcessor;

public class PersistMessageProcessorRegistrationTests
{
    public sealed class TestModule;

    [Fact]
    public void module_publisher_is_exposed_as_both_generic_and_non_generic_integration_event_publisher()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var untyped = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var typed = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher<TestModule>>();

        untyped.Should().BeOfType<PersistMessageIntegrationEventPublisher<TestModule>>();
        typed.Should().BeSameAs(untyped);
    }

    [Fact]
    public void module_publisher_is_scoped()
    {
        using var provider = BuildProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher<TestModule>>();
        var second = secondScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher<TestModule>>();

        first.Should().NotBeSameAs(second);
        firstScope
            .ServiceProvider.GetRequiredService<IIntegrationEventPublisher<TestModule>>()
            .Should()
            .BeSameAs(first);
    }

    [Fact]
    public async Task module_publisher_forwards_to_the_module_owned_processor()
    {
        var processor = Substitute.For<IPersistMessageProcessor<TestModule>>();
        using var provider = BuildProvider(processor);
        using var scope = provider.CreateScope();
        var envelope = new MessageEnvelope(new object(), new Dictionary<string, object?>());

        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(envelope);

        await processor.Received(1).PublishMessageAsync(envelope, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void legacy_persist_message_options_enabled_flag_no_longer_controls_the_module_worker()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["PersistMessageOptions:Enabled"] = "false" }
        );

        builder.AddPersistMessageProcessor<TestModule>("Test");

        builder
            .Services.Should()
            .Contain(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(PersistMessageBackgroundService<TestModule>)
            );
    }

    private static ServiceProvider BuildProvider(IPersistMessageProcessor<TestModule>? processor = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddPersistMessageProcessor<TestModule>("Test");
        builder.Services.Replace(
            ServiceDescriptor.Scoped(_ => processor ?? Substitute.For<IPersistMessageProcessor<TestModule>>())
        );

        return builder.Services.BuildServiceProvider();
    }
}
