using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test;

public class PersistMessagePublisherTests
{
    [Fact]
    public async Task publishes_the_original_envelope_and_cancellation_token_to_the_outbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor<global::Flight.FlightRoot>>();
        var publisher = new PersistMessageIntegrationEventPublisher<global::Flight.FlightRoot>(processor);
        var envelope = new MessageEnvelope(
            new PublishedEvent(Guid.NewGuid()),
            new Dictionary<string, object?> { ["CorrelationId"] = Guid.NewGuid() }
        );
        using var cancellation = new CancellationTokenSource();

        await publisher.PublishAsync(envelope, cancellation.Token);

        await processor.Received(1).PublishMessageAsync(envelope, cancellation.Token);
        processor.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task stores_the_original_internal_command_and_cancellation_token()
    {
        var processor = Substitute.For<IPersistMessageProcessor<global::Flight.FlightRoot>>();
        var publisher = new PersistMessageIntegrationEventPublisher<global::Flight.FlightRoot>(processor);
        var command = new ReserveStock(Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();

        await publisher.AddInternalCommandAsync(command, cancellation.Token);

        await processor.Received(1).AddInternalMessageAsync(command, cancellation.Token);
        processor.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task storage_failure_is_not_swallowed(bool internalCommand)
    {
        var processor = Substitute.For<IPersistMessageProcessor<global::Flight.FlightRoot>>();
        var failure = new InvalidOperationException("Outbox unavailable");
        processor
            .PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        processor
            .AddInternalMessageAsync(Arg.Any<ReserveStock>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        var publisher = new PersistMessageIntegrationEventPublisher<global::Flight.FlightRoot>(processor);

        Func<Task> act = () =>
            internalCommand
                ? publisher.AddInternalCommandAsync(new ReserveStock(Guid.NewGuid()))
                : publisher.PublishAsync(new MessageEnvelope(new PublishedEvent(Guid.NewGuid())));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task registration_resolves_the_scoped_adapter_and_preserves_a_custom_publisher(bool customPublisher)
    {
        var builder = WebApplication.CreateBuilder();
        var custom = Substitute.For<IIntegrationEventPublisher<global::Flight.FlightRoot>>();
        if (customPublisher)
            builder.Services.AddSingleton(custom);

        builder.AddPersistMessageProcessor<global::Flight.FlightRoot>("Flight");
        builder.Services.AddScoped<IPersistMessageProcessor<global::Flight.FlightRoot>>(_ =>
            Substitute.For<IPersistMessageProcessor<global::Flight.FlightRoot>>()
        );
        await using var provider = builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        var publisher = first.ServiceProvider.GetRequiredService<
            IIntegrationEventPublisher<global::Flight.FlightRoot>
        >();

        first
            .ServiceProvider.GetServices<IIntegrationEventPublisher<global::Flight.FlightRoot>>()
            .Should()
            .ContainSingle();
        if (customPublisher)
        {
            publisher.Should().BeSameAs(custom);
        }
        else
        {
            publisher.Should().BeOfType<PersistMessageIntegrationEventPublisher<global::Flight.FlightRoot>>();
            first
                .ServiceProvider.GetRequiredService<IIntegrationEventPublisher<global::Flight.FlightRoot>>()
                .Should()
                .BeSameAs(publisher);
            second
                .ServiceProvider.GetRequiredService<IIntegrationEventPublisher<global::Flight.FlightRoot>>()
                .Should()
                .NotBeSameAs(publisher);
            var envelope = new MessageEnvelope(new PublishedEvent(Guid.NewGuid()));
            await publisher.PublishAsync(envelope);
            await first
                .ServiceProvider.GetRequiredService<IPersistMessageProcessor<global::Flight.FlightRoot>>()
                .Received(1)
                .PublishMessageAsync(envelope, CancellationToken.None);
        }
    }

    private sealed record PublishedEvent(Guid Id) : IIntegrationEvent;

    private sealed record ReserveStock(Guid Id) : InternalCommand;
}
