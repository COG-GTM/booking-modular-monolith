using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.Tests;

public class EventDispatcherBoundaryTests
{
    [Fact]
    public async Task direct_integration_events_preserve_identity_order_and_cancellation_token()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = CreateDispatcher(publisher);
        using var cancellation = new CancellationTokenSource();
        var events = new[] { new PublishedEvent(Guid.NewGuid()), new PublishedEvent(Guid.NewGuid()) };

        await dispatcher.SendAsync(events, cancellationToken: cancellation.Token);

        publisher.Messages.Select(x => x.Message).Should().Equal(events);
        publisher.Messages[0].Message.Should().BeSameAs(events[0]);
        publisher.Messages[1].Message.Should().BeSameAs(events[1]);
        publisher.Tokens.Should().Equal(cancellation.Token, cancellation.Token);
        publisher.Messages.Should().AllSatisfy(x => x.Headers.Should().BeEmpty());
    }

    [Fact]
    public async Task later_header_provider_overrides_duplicate_keys_without_sharing_envelopes()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = CreateDispatcher(
            publisher,
            new Headers(new Dictionary<string, object?> { ["Tenant"] = "first", ["CorrelationId"] = "abc" }),
            new Headers(new Dictionary<string, object?> { ["Tenant"] = "last" })
        );

        await dispatcher.SendAsync(new[] { new PublishedEvent(Guid.NewGuid()), new PublishedEvent(Guid.NewGuid()) });

        publisher
            .Messages.Should()
            .AllSatisfy(x => x.Headers.Should().Contain("Tenant", "last").And.Contain("CorrelationId", "abc"));
        publisher.Messages[0].Headers["Tenant"] = "changed";
        publisher.Messages[1].Headers["Tenant"].Should().Be("last");
    }

    [Fact]
    public async Task empty_batch_does_not_read_headers_or_publish()
    {
        var publisher = new RecordingPublisher();
        var headers = new Headers(new Dictionary<string, object?>());

        await CreateDispatcher(publisher, headers).SendAsync(Array.Empty<IIntegrationEvent>());

        publisher.Messages.Should().BeEmpty();
        headers.Calls.Should().Be(0);
    }

    [Fact]
    public void mapper_registration_reuses_concrete_scoped_instance_but_not_across_scopes()
    {
        var services = new ServiceCollection();
        services.AddEventMapper<ScopedMapper>();
        services.AddEventMapper<ScopedMapper>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var mapper = first.ServiceProvider.GetRequiredService<ScopedMapper>();

        first.ServiceProvider.GetServices<IEventMapper>().Should().ContainSingle().Which.Should().BeSameAs(mapper);
        second.ServiceProvider.GetRequiredService<IEventMapper>().Should().NotBeSameAs(mapper);
    }

    private static EventDispatcher CreateDispatcher(
        RecordingPublisher publisher,
        params IEventHeadersProvider[] headers
    ) => new(Array.Empty<IEventMapper>(), NullLogger<EventDispatcher>.Instance, publisher, headers);

    private sealed record PublishedEvent(Guid Id) : IIntegrationEvent;

    private sealed class ScopedMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }

    private sealed class Headers(IReadOnlyDictionary<string, object?> values) : IEventHeadersProvider
    {
        public int Calls { get; private set; }

        public IReadOnlyDictionary<string, object?> GetHeaders()
        {
            Calls++;
            return values;
        }
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<MessageEnvelope> Messages { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();

        public Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default)
        {
            Messages.Add(messageEnvelope);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task AddInternalCommandAsync<TCommand>(
            TCommand internalCommand,
            CancellationToken cancellationToken = default
        )
            where TCommand : class, IInternalCommand =>
            throw new InvalidOperationException("Unexpected internal command.");
    }
}
