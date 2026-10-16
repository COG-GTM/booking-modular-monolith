using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.JwtTests;

public class AuthHeaderHandlerTests
{
    private sealed class JwtCapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static (HttpMessageInvoker Invoker, JwtCapturingHandler Inner) CreateInvoker(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        var inner = new JwtCapturingHandler();
        var handler = new AuthHeaderHandler(accessor) { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), inner);
    }

    [Fact]
    public async Task bearer_prefixed_token_is_forwarded_as_bearer_scheme_with_token_parameter()
    {
        var (invoker, inner) = CreateInvoker("Bearer abc123");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://x"), CancellationToken.None);

        inner.Captured!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.Captured.Headers.Authorization.Parameter.Should().Be("abc123");
    }

    [Fact]
    public async Task raw_token_is_forwarded_as_the_parameter()
    {
        var (invoker, inner) = CreateInvoker("abc123");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://x"), CancellationToken.None);

        inner.Captured!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.Captured.Headers.Authorization.Parameter.Should().Be("abc123");
    }
}
