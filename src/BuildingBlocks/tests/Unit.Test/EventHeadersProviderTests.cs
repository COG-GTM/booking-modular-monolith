using System.Security.Claims;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class EventHeadersProviderTests
{
    [Fact]
    public void add_event_dispatcher_is_idempotent_and_defaults_to_the_empty_headers_provider()
    {
        var services = new ServiceCollection();

        services.AddEventDispatcher().AddEventDispatcher();

        services.Where(d => d.ServiceType == typeof(IEventDispatcher)).Should().ContainSingle();
        services
            .Where(d => d.ServiceType == typeof(IEventHeadersProvider))
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be<DefaultEventHeadersProvider>();
    }

    [Fact]
    public async Task envelopes_carry_no_headers_when_only_the_default_provider_is_registered()
    {
        var publisher = new RecordingPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventDispatcher();
        services.AddEventMapper<FlightMapper>();
        services.AddSingleton<IIntegrationEventPublisher>(publisher);
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        using var scope = provider.CreateScope();
        var id = Guid.NewGuid();

        await scope
            .ServiceProvider.GetRequiredService<IEventDispatcher>()
            .SendAsync(new IDomainEvent[] { new FlightCreatedDomainEvent(id) });

        publisher.Published.Should().ContainSingle().Which.Headers.Should().BeEmpty();
    }

    [Fact]
    public void default_provider_returns_a_fresh_dictionary_on_every_call()
    {
        var sut = new DefaultEventHeadersProvider();

        var first = sut.GetHeaders();
        first["CorrelationId"] = "mutated";
        var second = sut.GetHeaders();

        second.Should().NotBeSameAs(first);
        second.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void http_context_headers_replace_the_default_provider_regardless_of_registration_order(
        bool registerHeadersFirst
    )
    {
        var services = new ServiceCollection();

        if (registerHeadersFirst)
        {
            services.AddHttpContextEventHeaders();
            services.AddEventDispatcher();
        }
        else
        {
            services.AddEventDispatcher();
            services.AddHttpContextEventHeaders();
        }

        services
            .Where(d => d.ServiceType == typeof(IEventHeadersProvider))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(HttpContextEventHeadersProvider) && d.Lifetime == ServiceLifetime.Scoped
            );
        services.Should().Contain(d => d.ServiceType == typeof(IHttpContextAccessor));
    }

    [Fact]
    public void http_context_provider_reads_correlation_id_and_user_claims_from_the_current_request()
    {
        var correlationId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Items["correlationId"] = correlationId.ToString("N");
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "user-42"), new Claim(ClaimTypes.Name, "sam") },
                "test"
            )
        );
        var sut = new HttpContextEventHeadersProvider(new HttpContextAccessor { HttpContext = httpContext });

        var headers = sut.GetHeaders();

        headers.Should().HaveCount(3);
        headers["CorrelationId"].Should().Be(correlationId);
        headers["UserId"].Should().Be("user-42");
        headers["UserName"].Should().Be("sam");
    }

    [Fact]
    public void http_context_provider_returns_null_values_when_there_is_no_active_request()
    {
        var sut = new HttpContextEventHeadersProvider(new HttpContextAccessor { HttpContext = null });

        var headers = sut.GetHeaders();

        headers.Keys.Should().BeEquivalentTo("CorrelationId", "UserId", "UserName");
        headers.Values.Should().AllSatisfy(value => value.Should().BeNull());
    }

    [Fact]
    public void http_context_provider_leaves_user_headers_null_for_anonymous_requests()
    {
        var httpContext = new DefaultHttpContext();
        var sut = new HttpContextEventHeadersProvider(new HttpContextAccessor { HttpContext = httpContext });

        var headers = sut.GetHeaders();

        headers["CorrelationId"].Should().BeOfType<Guid>().Which.Should().NotBeEmpty();
        headers["UserId"].Should().BeNull();
        headers["UserName"].Should().BeNull();
    }
}
