using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test;

public class AuthHeaderHandlerTests
{
    [Fact]
    public async Task should_send_no_authorization_without_http_context()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        await AssertAuthorization(accessor, null, null);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("Bearer abc", "Bearer", "abc")]
    [InlineData("Basic dXNlcjpwYXNz", "Basic", "dXNlcjpwYXNz")]
    [InlineData("invalid: header", null, null)]
    public async Task should_forward_only_parseable_incoming_authorization(
        string header,
        string scheme,
        string parameter
    )
    {
        var context = new DefaultHttpContext();
        if (header is not null)
            context.Request.Headers.Authorization = header;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);

        await AssertAuthorization(accessor, scheme, parameter);
    }

    private static async Task AssertAuthorization(IHttpContextAccessor accessor, string scheme, string parameter)
    {
        var inner = new RecordingHandler();
        using var handler = new AuthHeaderHandler(accessor) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        inner.Request.Should().BeSameAs(request);
        if (scheme is null)
        {
            inner.Request.Headers.Contains("Authorization").Should().BeFalse();
        }
        else
        {
            inner.Request.Headers.Authorization.Scheme.Should().Be(scheme);
            inner.Request.Headers.Authorization.Parameter.Should().Be(parameter);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
