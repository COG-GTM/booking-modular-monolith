using System.Security.Claims;
using BuildingBlocks.Core;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Tests;

public class HttpContextEventHeadersProviderTests
{
    [Fact]
    public void emits_correlation_id_and_user_claims_from_current_request()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Name, "samh"),
            })),
        };
        var correlationId = Guid.NewGuid();
        httpContext.Items["correlationId"] = correlationId.ToString();

        var headers = new HttpContextEventHeadersProvider(new HttpContextAccessor { HttpContext = httpContext }).GetHeaders();

        headers["UserId"].Should().Be("42");
        headers["UserName"].Should().Be("samh");
        headers["CorrelationId"].Should().Be(correlationId);
    }

    [Fact]
    public void outside_a_request_headers_are_null()
    {
        var headers = new HttpContextEventHeadersProvider(new HttpContextAccessor()).GetHeaders();

        headers.Values.Should().AllSatisfy(v => v.Should().BeNull());
    }

    [Fact]
    public void registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddHttpContextEventHeaders();
        services.AddHttpContextEventHeaders();

        services.Count(x => x.ServiceType == typeof(IEventHeadersProvider)).Should().Be(1);
    }
}
