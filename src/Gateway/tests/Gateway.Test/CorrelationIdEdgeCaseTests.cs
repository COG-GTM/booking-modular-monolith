using Gateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Gateway.Test;

public class CorrelationIdEdgeCaseTests
{
    [Theory]
    [InlineData("D")]
    [InlineData("B")]
    [InlineData("P")]
    public async Task normalizes_guid_formats_before_downstream_execution(string format)
    {
        var id = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = id.ToString(format).ToUpperInvariant();
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            Assert.Equal(id.ToString("N"), ctx.Request.Headers[CorrelationIdMiddleware.HeaderName]);
            Assert.Equal(id.ToString("N"), ctx.Items[CorrelationIdMiddleware.HeaderName]);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task replaces_ambiguous_multiple_header_values_with_one_guid()
    {
        var ids = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = new StringValues(ids);
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var forwarded = Assert.Single(context.Request.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.True(Guid.TryParseExact(forwarded, "N", out _));
        Assert.DoesNotContain(forwarded, ids);
        Assert.Equal(forwarded, context.Items[CorrelationIdMiddleware.HeaderName]);
    }
}
