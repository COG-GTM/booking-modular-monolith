using System.Security.Claims;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Core;

public class EventDispatcherTests
{
    private readonly IEventMapper _eventMapper = Substitute.For<IEventMapper>();
    private readonly IPersistMessageProcessor _persistMessageProcessor = Substitute.For<IPersistMessageProcessor>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

    private EventDispatcher CreateSut()
    {
        return new EventDispatcher(
            Substitute.For<IServiceScopeFactory>(),
            _eventMapper,
            Substitute.For<ILogger<EventDispatcher>>(),
            _persistMessageProcessor,
            _httpContextAccessor
        );
    }

    [Fact]
    public async Task send_with_no_events_should_not_touch_mapper_or_persist_processor()
    {
        var sut = CreateSut();

        await sut.SendAsync(Array.Empty<FakeDomainEvent>());

        _eventMapper.DidNotReceiveWithAnyArgs().MapToIntegrationEvent(default!);
        await _persistMessageProcessor
            .DidNotReceiveWithAnyArgs()
            .PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_domain_event_should_map_to_integration_event_and_publish_it_to_outbox()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid(), "created");
        var integrationEvent = new FakeIntegrationEvent(Guid.NewGuid(), "created");
        _eventMapper.MapToIntegrationEvent(domainEvent).Returns(integrationEvent);
        var sut = CreateSut();

        await sut.SendAsync(new IDomainEvent[] { domainEvent });

        await _persistMessageProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, integrationEvent)),
                Arg.Any<CancellationToken>()
            );
        await _persistMessageProcessor
            .DidNotReceiveWithAnyArgs()
            .AddInternalMessageAsync(Arg.Any<IInternalCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_domain_event_that_maps_to_nothing_should_publish_nothing()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid(), "ignored");
        _eventMapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        var sut = CreateSut();

        await sut.SendAsync(new IDomainEvent[] { domainEvent });

        await _persistMessageProcessor
            .DidNotReceiveWithAnyArgs()
            .PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task send_domain_event_marked_with_have_integration_event_should_publish_wrapped_event()
    {
        var domainEvent = new FakeDomainEventWithIntegrationEvent(Guid.NewGuid(), "wrapped");
        var sut = CreateSut();

        await sut.SendAsync(new IDomainEvent[] { domainEvent });

        await _persistMessageProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(e =>
                    e.Message is IntegrationEventWrapper<FakeDomainEventWithIntegrationEvent>
                    && ((IntegrationEventWrapper<FakeDomainEventWithIntegrationEvent>)e.Message).DomainEvent
                        == domainEvent
                ),
                Arg.Any<CancellationToken>()
            );
        _eventMapper.DidNotReceiveWithAnyArgs().MapToIntegrationEvent(default!);
    }

    [Fact]
    public async Task send_integration_events_should_publish_each_of_them_without_mapping()
    {
        var first = new FakeIntegrationEvent(Guid.NewGuid(), "first");
        var second = new FakeIntegrationEvent(Guid.NewGuid(), "second");
        var sut = CreateSut();

        await sut.SendAsync(new IIntegrationEvent[] { first, second });

        await _persistMessageProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, first)),
                Arg.Any<CancellationToken>()
            );
        await _persistMessageProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, second)),
                Arg.Any<CancellationToken>()
            );
        _eventMapper.DidNotReceiveWithAnyArgs().MapToIntegrationEvent(default!);
    }

    [Fact]
    public async Task send_single_event_overload_should_publish_that_event()
    {
        var integrationEvent = new FakeIntegrationEvent(Guid.NewGuid(), "single");
        var sut = CreateSut();

        await sut.SendAsync(integrationEvent);

        await _persistMessageProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(e => ReferenceEquals(e.Message, integrationEvent)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task send_with_internal_command_request_type_should_store_mapped_internal_commands()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid(), "internal");
        var internalCommand = new FakeInternalCommand(Guid.NewGuid(), "internal");
        _eventMapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        _eventMapper.MapToInternalCommand(domainEvent).Returns(internalCommand);
        var sut = CreateSut();

        await sut.SendAsync(new IDomainEvent[] { domainEvent }, typeof(FakeInternalCommand));

        await _persistMessageProcessor
            .Received(1)
            .AddInternalMessageAsync(
                Arg.Is<IInternalCommand>(c => ReferenceEquals(c, internalCommand)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task send_with_non_internal_command_request_type_should_not_map_internal_commands()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid(), "domain");
        _eventMapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        var sut = CreateSut();

        await sut.SendAsync(new IDomainEvent[] { domainEvent }, typeof(FakeCommand));

        _eventMapper.DidNotReceiveWithAnyArgs().MapToInternalCommand(default!);
        await _persistMessageProcessor
            .DidNotReceiveWithAnyArgs()
            .AddInternalMessageAsync(Arg.Any<IInternalCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task published_envelope_should_carry_correlation_and_user_headers_from_http_context()
    {
        var correlationId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Items["correlationId"] = correlationId.ToString();
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "42"), new Claim(ClaimTypes.Name, "alice") }
            )
        );
        _httpContextAccessor.HttpContext.Returns(httpContext);
        var integrationEvent = new FakeIntegrationEvent(Guid.NewGuid(), "headers");
        var sut = CreateSut();

        await sut.SendAsync(new IIntegrationEvent[] { integrationEvent });

        var envelope = (MessageEnvelope)_persistMessageProcessor.ReceivedCalls().Single().GetArguments()[0]!;
        envelope.Headers["CorrelationId"].Should().Be(correlationId);
        envelope.Headers["UserId"].Should().Be("42");
        envelope.Headers["UserName"].Should().Be("alice");
    }

    [Fact]
    public async Task published_envelope_without_http_context_should_have_null_header_values()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var sut = CreateSut();

        await sut.SendAsync(new IIntegrationEvent[] { new FakeIntegrationEvent(Guid.NewGuid(), "no-http") });

        var envelope = (MessageEnvelope)_persistMessageProcessor.ReceivedCalls().Single().GetArguments()[0]!;
        envelope.Headers.Keys.Should().BeEquivalentTo("CorrelationId", "UserId", "UserName");
        envelope.Headers.Values.Should().AllSatisfy(v => v.Should().BeNull());
    }
}
