using System.Net;
using System.Net.Http.Headers;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test;

public class AuthHeaderHandlerTests
{
    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("BEARER abc")]
    public async Task should_forward_bearer_token_with_canonical_scheme(string header)
    {
        var authorization = await SendAsync(header);

        authorization.Should().NotBeNull();
        authorization!.Scheme.Should().Be("Bearer");
        authorization.Parameter.Should().Be("abc");
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    [InlineData(null)]
    public async Task should_not_forward_non_bearer_or_missing_token(string? header)
    {
        var authorization = await SendAsync(header);

        authorization.Should().BeNull();
    }

    [Fact]
    public async Task should_not_forward_authorization_without_http_context()
    {
        var authorization = await SendAsync(null, hasHttpContext: false);

        authorization.Should().BeNull();
    }

    [Fact]
    public async Task should_not_forward_malformed_authorization()
    {
        const string header = "Bearer \"unterminated";
        AuthenticationHeaderValue.TryParse(header, out _).Should().BeFalse();

        var authorization = await SendAsync(header);

        authorization.Should().BeNull();
    }

    private static async Task<AuthenticationHeaderValue?> SendAsync(string? header, bool hasHttpContext = true)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        if (hasHttpContext)
        {
            var context = new DefaultHttpContext();
            if (header is not null)
            {
                context.Request.Headers.Authorization = header;
            }

            accessor.HttpContext.Returns(context);
        }

        var recordingHandler = new RecordingHandler();
        using var handler = new AuthHeaderHandler(accessor) { InnerHandler = recordingHandler };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        return recordingHandler.Authorization;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
