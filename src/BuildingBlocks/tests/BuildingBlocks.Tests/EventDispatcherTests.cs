using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.Tests;

public class EventDispatcherTests
{
    [Fact]
    public async Task single_service_registers_only_its_own_mapper_and_publishes_mapped_event()
    {
        await using var provider = BuildProvider(services => services.AddEventMapper<OrderEventMapper>());
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        var publisher = scope.ServiceProvider.GetRequiredService<RecordingPublisher>();

        await dispatcher.SendAsync(new OrderPlaced(Guid.NewGuid()));

        publisher.Published.Should().ContainSingle()
            .Which.Message.Should().BeOfType<OrderPlacedIntegration>();
        scope.ServiceProvider.GetServices<IEventMapper>().Should().ContainSingle();
    }

    [Fact]
    public async Task mappers_from_several_modules_are_composed_without_host_wiring()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddEventMapper<OrderEventMapper>();
            services.AddEventMapper<PaymentEventMapper>();
            services.AddEventMapper<OrderEventMapper>();
        });
        await using var scope = provider.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        var publisher = scope.ServiceProvider.GetRequiredService<RecordingPublisher>();

        await dispatcher.SendAsync(new IDomainEvent[] { new OrderPlaced(Guid.NewGuid()), new PaymentTaken(Guid.NewGuid()) });

        publisher.Published.Select(x => x.Message!.GetType()).Should()
            .BeEquivalentTo(new[] { typeof(OrderPlacedIntegration), typeof(PaymentTakenIntegration) });
        scope.ServiceProvider.GetServices<IEventMapper>().Should().HaveCount(2);
    }

    [Fact]
    public async Task unmapped_domain_event_is_not_published()
    {
        await using var provider = BuildProvider(services => services.AddEventMapper<OrderEventMapper>());
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync(new PaymentTaken(Guid.NewGuid()));

        scope.ServiceProvider.GetRequiredService<RecordingPublisher>().Published.Should().BeEmpty();
    }

    [Fact]
    public async Task dispatcher_works_with_no_mappers_registered()
    {
        await using var provider = BuildProvider(_ => { });
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync(new OrderPlaced(Guid.NewGuid()));

        await act.Should().NotThrowAsync();
        scope.ServiceProvider.GetRequiredService<RecordingPublisher>().Published.Should().BeEmpty();
    }

    [Fact]
    public async Task internal_commands_are_handed_to_the_publisher()
    {
        await using var provider = BuildProvider(services => services.AddEventMapper<OrderEventMapper>());
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
            .SendAsync(new OrderPlaced(Guid.NewGuid()), typeof(IInternalCommand));

        scope.ServiceProvider.GetRequiredService<RecordingPublisher>().InternalCommands.Should().ContainSingle()
            .Which.Should().BeOfType<ReserveStock>();
    }

    [Fact]
    public async Task headers_come_from_registered_header_providers()
    {
        await using var provider = BuildProvider(services =>
        {
            services.AddEventMapper<OrderEventMapper>();
            services.AddSingleton<IEventHeadersProvider>(new StaticHeaders("CorrelationId", "abc"));
            services.AddSingleton<IEventHeadersProvider>(new StaticHeaders("Tenant", "t1"));
        });
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync(new OrderPlaced(Guid.NewGuid()));

        var headers = scope.ServiceProvider.GetRequiredService<RecordingPublisher>().Published.Single().Headers;
        headers.Should().Contain("CorrelationId", "abc").And.Contain("Tenant", "t1");
    }

    [Fact]
    public void add_event_dispatcher_does_not_override_a_custom_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEventDispatcher, NoopDispatcher>();
        services.AddEventDispatcher();

        services.Where(x => x.ServiceType == typeof(IEventDispatcher)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be<NoopDispatcher>();
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<RecordingPublisher>();
        services.AddScoped<IIntegrationEventPublisher>(sp => sp.GetRequiredService<RecordingPublisher>());
        services.AddEventDispatcher();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed record OrderPlaced(Guid Id) : IDomainEvent;

    private sealed record PaymentTaken(Guid Id) : IDomainEvent;

    private sealed record OrderPlacedIntegration(Guid Id) : IIntegrationEvent;

    private sealed record PaymentTakenIntegration(Guid Id) : IIntegrationEvent;

    private sealed record ReserveStock(Guid Id) : InternalCommand;

    private sealed class OrderEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
            @event is OrderPlaced e ? new OrderPlacedIntegration(e.Id) : null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) =>
            @event is OrderPlaced e ? new ReserveStock(e.Id) : null;
    }

    private sealed class PaymentEventMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
            @event is PaymentTaken e ? new PaymentTakenIntegration(e.Id) : null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<MessageEnvelope> Published { get; } = new();

        public List<IInternalCommand> InternalCommands { get; } = new();

        public Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default)
        {
            Published.Add(messageEnvelope);
            return Task.CompletedTask;
        }

        public Task AddInternalCommandAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
            where TCommand : class, IInternalCommand
        {
            InternalCommands.Add(internalCommand);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticHeaders(string key, string value) : IEventHeadersProvider
    {
        public IReadOnlyDictionary<string, object?> GetHeaders() => new Dictionary<string, object?> { [key] = value };
    }

    private sealed class NoopDispatcher : IEventDispatcher
    {
        public Task SendAsync<T>(IReadOnlyList<T> events, Type? type = null, CancellationToken cancellationToken = default)
            where T : IEvent => Task.CompletedTask;

        public Task SendAsync<T>(T @event, Type? type = null, CancellationToken cancellationToken = default)
            where T : IEvent => Task.CompletedTask;
    }
}
