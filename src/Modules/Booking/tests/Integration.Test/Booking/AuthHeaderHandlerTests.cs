using System.Net;
using System.Net.Http.Headers;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Integration.Test.Booking;

public class AuthHeaderHandlerTests
{
    [Theory]
    [InlineData("Bearer")]
    [InlineData("bearer")]
    public async Task should_forward_bearer_token_regardless_of_scheme_casing(string scheme)
    {
        var outgoing = await SendThroughHandler($"{scheme} test-token");

        outgoing.Should().NotBeNull();
        outgoing!.Scheme.Should().Be("Bearer");
        outgoing.Parameter.Should().Be("test-token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task should_not_forward_authorization_when_request_has_no_bearer_token(string? header)
    {
        var outgoing = await SendThroughHandler(header);

        outgoing.Should().BeNull();
    }

    private static async Task<AuthenticationHeaderValue?> SendThroughHandler(string? incomingAuthorization)
    {
        var context = new DefaultHttpContext();
        if (incomingAuthorization is not null)
        {
            context.Request.Headers.Authorization = incomingAuthorization;
        }

        var capturing = new CapturingHandler();
        var handler = new AuthHeaderHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = capturing,
        };

        using var client = new HttpClient(handler);
        await client.GetAsync("https://localhost/grpc-test");

        return capturing.Authorization;
    }

    private sealed class CapturingHandler : HttpMessageHandler
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
