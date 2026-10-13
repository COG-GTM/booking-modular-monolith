using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class EventDispatcherRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventDispatcher();
        configure(services);
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    [Fact]
    public void add_event_mapper_is_idempotent_and_resolvable_by_concrete_type()
    {
        using var provider = BuildProvider(s => s.AddEventMapper<FlightMapper>().AddEventMapper<FlightMapper>());
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetServices<IEventMapper>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<FlightMapper>();
        scope.ServiceProvider.GetRequiredService<FlightMapper>().Should().NotBeNull();
    }

    [Fact]
    public async Task dispatcher_composes_mappers_registered_by_each_module()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildProvider(s =>
        {
            s.AddEventMapper<FlightMapper>();
            s.AddEventMapper<BookingMapper>();
            s.AddSingleton<IIntegrationEventPublisher>(publisher);
        });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        var id = Guid.NewGuid();

        await dispatcher.SendAsync(
            new IDomainEvent[] { new FlightCreatedDomainEvent(id), new BookingCreatedDomainEvent(id) }
        );

        publisher
            .Published.Select(x => x.Message)
            .Should()
            .Equal(new object[] { new FlightCreatedIntegrationEvent(id), new BookingCreatedIntegrationEvent(id) });
    }

    [Fact]
    public async Task single_service_registers_only_its_own_mapper()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildProvider(s =>
        {
            s.AddEventMapper<BookingMapper>();
            s.AddSingleton<IIntegrationEventPublisher>(publisher);
        });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

        await dispatcher.SendAsync(new IDomainEvent[] { new FlightCreatedDomainEvent(Guid.NewGuid()) });

        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task internal_commands_are_forwarded_to_the_publisher()
    {
        var publisher = new RecordingPublisher();
        using var provider = BuildProvider(s =>
        {
            s.AddEventMapper<FlightMapper>();
            s.AddSingleton<IIntegrationEventPublisher>(publisher);
        });
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        var id = Guid.NewGuid();

        await dispatcher.SendAsync(new IDomainEvent[] { new FlightCreatedDomainEvent(id) }, typeof(IInternalCommand));

        publisher.InternalCommands.Should().ContainSingle().Which.Should().Be(new CreateFlightReadModel(id));
    }

    [Fact]
    public async Task dispatcher_resolves_without_publisher_and_fails_only_when_publishing()
    {
        using var provider = BuildProvider(s => s.AddEventMapper<FlightMapper>());
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

        await dispatcher.SendAsync(new IDomainEvent[] { new BookingCreatedDomainEvent(Guid.NewGuid()) });

        var act = () => dispatcher.SendAsync(new IDomainEvent[] { new FlightCreatedDomainEvent(Guid.NewGuid()) });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IIntegrationEventPublisher*");
    }

    [Fact]
    public async Task http_context_headers_are_attached_when_registered()
    {
        var publisher = new RecordingPublisher();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["correlationId"] = "corr-1";
        using var provider = BuildProvider(s =>
        {
            s.AddHttpContextEventHeaders();
            s.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
            s.AddEventMapper<FlightMapper>();
            s.AddSingleton<IIntegrationEventPublisher>(publisher);
        });
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<IEventHeadersProvider>()
            .Should()
            .BeOfType<HttpContextEventHeadersProvider>();

        await scope
            .ServiceProvider.GetRequiredService<IEventDispatcher>()
            .SendAsync(new IDomainEvent[] { new FlightCreatedDomainEvent(Guid.NewGuid()) });

        publisher
            .Published.Should()
            .ContainSingle()
            .Which.Headers.Should()
            .ContainKeys("CorrelationId", "UserId", "UserName");
    }

    [Fact]
    public async Task persist_message_publisher_delegates_to_persist_message_processor()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var sut = new PersistMessageIntegrationEventPublisher(processor);
        var envelope = new MessageEnvelope(new FlightCreatedIntegrationEvent(Guid.NewGuid()));
        var command = new CreateFlightReadModel(Guid.NewGuid());

        await sut.PublishAsync(envelope);
        await sut.AddInternalMessageAsync(command);

        await processor.Received(1).PublishMessageAsync(envelope, Arg.Any<CancellationToken>());
        await processor.Received(1).AddInternalMessageAsync(command, Arg.Any<CancellationToken>());
    }
}
