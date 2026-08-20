using System.Security.Claims;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test.Web;

public class HttpContextEventHeadersProviderTests
{
    [Fact]
    public void get_headers_should_include_correlation_id_user_id_and_user_name_from_http_context()
    {
        var correlationId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Items["correlationId"] = correlationId.ToString();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Name, "test-user"),
        }));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);

        var headers = new HttpContextEventHeadersProvider(accessor).GetHeaders();

        headers["CorrelationId"].Should().Be(correlationId);
        headers["UserId"].Should().Be("user-1");
        headers["UserName"].Should().Be("test-user");
    }

    [Fact]
    public void get_headers_should_return_null_values_when_http_context_is_missing()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var headers = new HttpContextEventHeadersProvider(accessor).GetHeaders();

        headers.Keys.Should().BeEquivalentTo("CorrelationId", "UserId", "UserName");
        headers["CorrelationId"].Should().BeNull();
        headers["UserId"].Should().BeNull();
        headers["UserName"].Should().BeNull();
    }
}
