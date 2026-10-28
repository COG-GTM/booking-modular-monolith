using System.Security.Claims;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class HttpEventHeadersProviderTests
{
    [Fact]
    public void ReadsCorrelationAndUserClaimsFromCurrentHttpContext()
    {
        var correlationId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-42"), new Claim(ClaimTypes.Name, "Jane")],
                    "test"
                )
            ),
        };
        context.Items["correlationId"] = correlationId.ToString();
        var provider = new HttpEventHeadersProvider(new HttpContextAccessor { HttpContext = context });

        var headers = provider.GetHeaders();

        Assert.Equal(3, headers.Count);
        Assert.Equal(correlationId, headers["CorrelationId"]);
        Assert.Equal("user-42", headers["UserId"]);
        Assert.Equal("Jane", headers["UserName"]);
    }

    [Fact]
    public void ReturnsNullValuesForEveryKeyOutsideAnHttpRequest()
    {
        var provider = new HttpEventHeadersProvider(new HttpContextAccessor());

        var headers = provider.GetHeaders();

        Assert.Equal(["CorrelationId", "UserId", "UserName"], headers.Keys.Order(StringComparer.Ordinal));
        Assert.All(headers.Values, Assert.Null);
    }

    [Fact]
    public void ReturnsNullUserHeadersAndAGeneratedCorrelationIdForAnonymousRequests()
    {
        var provider = new HttpEventHeadersProvider(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var headers = provider.GetHeaders();

        Assert.IsType<Guid>(headers["CorrelationId"]);
        Assert.NotEqual(Guid.Empty, headers["CorrelationId"]);
        Assert.Null(headers["UserId"]);
        Assert.Null(headers["UserName"]);
    }

    [Fact]
    public void ReturnsAFreshDictionaryPerCall()
    {
        var provider = new HttpEventHeadersProvider(new HttpContextAccessor());

        var first = provider.GetHeaders();
        first["Extra"] = "mutated";

        Assert.False(provider.GetHeaders().ContainsKey("Extra"));
    }
}
