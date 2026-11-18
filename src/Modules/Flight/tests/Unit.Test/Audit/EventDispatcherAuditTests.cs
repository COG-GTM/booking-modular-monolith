using System.Security.Claims;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Audit;

public class EventDispatcherAuditTests
{
    [Theory]
    [InlineData("sub", "name")]
    [InlineData(ClaimTypes.NameIdentifier, ClaimTypes.Name)]
    public async Task published_headers_should_use_subject_and_name_claims(string subjectType, string nameType)
    {
        var subject = Guid.NewGuid().ToString();
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(subjectType, subject), new Claim(nameType, "alice") }, "Bearer")
        );

        var envelope = await Publish(user);

        envelope.Headers["UserId"].Should().Be(subject);
        envelope.Headers["UserName"].Should().Be("alice");
    }

    [Fact]
    public async Task published_headers_should_prefer_unmapped_claims()
    {
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim("sub", "jwt-subject"),
                    new Claim("name", "jwt-name"),
                    new Claim(ClaimTypes.NameIdentifier, "mapped-subject"),
                    new Claim(ClaimTypes.Name, "mapped-name"),
                },
                "Bearer"
            )
        );

        var envelope = await Publish(user);

        envelope.Headers["UserId"].Should().Be("jwt-subject");
        envelope.Headers["UserName"].Should().Be("jwt-name");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task published_headers_should_be_null_without_user_claims(bool missingContext)
    {
        var envelope = await Publish(new ClaimsPrincipal(new ClaimsIdentity()), missingContext);

        envelope.Headers.Should().ContainKey("UserId").WhoseValue.Should().BeNull();
        envelope.Headers.Should().ContainKey("UserName").WhoseValue.Should().BeNull();
    }

    private static async Task<MessageEnvelope> Publish(ClaimsPrincipal user, bool missingContext = false)
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(missingContext ? null : new DefaultHttpContext { User = user });
        var dispatcher = new EventDispatcher(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IEventMapper>(),
            NullLogger<EventDispatcher>.Instance,
            processor,
            accessor
        );
        var message = new AuditIntegrationEvent();

        await dispatcher.SendAsync<IIntegrationEvent>(message);

        await processor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(x => ReferenceEquals(x.Message, message)),
                Arg.Any<CancellationToken>()
            );
        return (MessageEnvelope)processor.ReceivedCalls().Single().GetArguments()[0]!;
    }

    private record AuditIntegrationEvent : IIntegrationEvent;
}
