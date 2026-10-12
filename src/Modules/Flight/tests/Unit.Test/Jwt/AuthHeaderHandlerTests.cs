using System.Net;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test.Jwt;

public class AuthHeaderHandlerTests
{
    [Theory]
    [InlineData("Bearer token-123")]
    [InlineData("bearer token-123")]
    [InlineData("BEARER token-123")]
    public async Task should_forward_bearer_credential_regardless_of_scheme_casing(string incomingAuthorization)
    {
        // Act
        var outgoingRequest = await SendAsync(incomingAuthorization);

        // Assert
        outgoingRequest.Headers.Authorization.Should().NotBeNull();
        outgoingRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        outgoingRequest.Headers.Authorization.Parameter.Should().Be("token-123");
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    [InlineData("")]
    public async Task should_not_forward_authorization_when_no_bearer_credential_is_present(
        string incomingAuthorization
    )
    {
        // Act
        var outgoingRequest = await SendAsync(incomingAuthorization);

        // Assert
        outgoingRequest.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task should_not_forward_authorization_when_request_has_no_authorization_header()
    {
        // Act
        var outgoingRequest = await SendAsync(incomingAuthorization: null);

        // Assert
        outgoingRequest.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task should_not_forward_authorization_when_there_is_no_http_context()
    {
        // Act
        var outgoingRequest = await SendAsync(incomingAuthorization: null, withHttpContext: false);

        // Assert
        outgoingRequest.Headers.Authorization.Should().BeNull();
    }

    private static async Task<HttpRequestMessage> SendAsync(string? incomingAuthorization, bool withHttpContext = true)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();

        if (withHttpContext)
        {
            var httpContext = new DefaultHttpContext();

            if (incomingAuthorization is not null)
            {
                httpContext.Request.Headers.Authorization = incomingAuthorization;
            }

            httpContextAccessor.HttpContext.Returns(httpContext);
        }
        else
        {
            httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        }

        var innerHandler = new CapturingHandler();
        var handler = new AuthHeaderHandler(httpContextAccessor) { InnerHandler = innerHandler };
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://flight.test/"), CancellationToken.None);

        return innerHandler.LastRequest!;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }
}
