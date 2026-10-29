using System.Security.Claims;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class OutboxDispatchCompositionTests
{
    [Fact]
    public async Task dispatcher_publishes_mapped_events_with_request_headers_through_the_outbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        var correlationId = Guid.NewGuid();
        var httpContext = CreateHttpContext(correlationId);
        await using var provider = CreateProvider(processor, httpContext);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync(domainEvent);

        processor.Received(1).PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        var envelope = processor
            .ReceivedCalls()
            .Single()
            .GetArguments()[0]
            .Should()
            .BeOfType<MessageEnvelope>()
            .Subject;
        envelope.Message.Should().Be(new TestIntegrationEvent(domainEvent.Id));
        envelope
            .Headers.Should()
            .Contain("CorrelationId", correlationId)
            .And.Contain("UserId", "42")
            .And.Contain("UserName", "samh");
    }

    [Fact]
    public async Task dispatcher_sends_mapped_internal_commands_to_the_outbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        await using var provider = CreateProvider(processor, CreateHttpContext(Guid.NewGuid()));
        await using var scope = provider.CreateAsyncScope();

        await scope
            .ServiceProvider.GetRequiredService<IEventDispatcher>()
            .SendAsync(domainEvent, typeof(IInternalCommand));

        processor
            .Received(1)
            .AddInternalMessageAsync(
                Arg.Is<TestCommand>(command => command.Id == domainEvent.Id),
                Arg.Any<CancellationToken>()
            );
    }

    private static ServiceProvider CreateProvider(IPersistMessageProcessor processor, HttpContext httpContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPersistMessageProcessor>(processor);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        services.AddEventDispatcher();
        services.AddEventMapper<TestMapper>();
        services.AddHttpContextEventHeaders();
        services.AddScoped<IIntegrationEventPublisher, PersistMessageIntegrationEventPublisher>();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static DefaultHttpContext CreateHttpContext(Guid correlationId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "42"), new Claim(ClaimTypes.Name, "samh")],
                    "test"
                )
            ),
        };
        httpContext.Items["correlationId"] = correlationId.ToString();
        return httpContext;
    }

    private sealed record TestDomainEvent(Guid Id) : IDomainEvent;

    private sealed record TestIntegrationEvent(Guid Id) : IIntegrationEvent;

    private sealed record TestCommand(Guid Id) : IInternalCommand;

    private sealed class TestMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
            @event is TestDomainEvent testEvent ? new TestIntegrationEvent(testEvent.Id) : null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) =>
            @event is TestDomainEvent testEvent ? new TestCommand(testEvent.Id) : null;
    }
}
