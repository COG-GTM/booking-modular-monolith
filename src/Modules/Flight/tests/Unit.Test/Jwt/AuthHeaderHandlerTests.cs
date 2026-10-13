using System.Net;
using System.Net.Http.Headers;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Unit.Test.Jwt;

public class AuthHeaderHandlerTests
{
    private const string Token = "incoming-user-token";

    [Theory]
    [InlineData("Bearer")]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    public async Task should_forward_bearer_token_to_https_destination_regardless_of_scheme_casing(string scheme)
    {
        // Act
        var outgoing = await SendThroughHandler($"{scheme} {Token}", "https://flight.internal/grpc");

        // Assert
        outgoing.Should().NotBeNull();
        outgoing!.Scheme.Should().Be("Bearer");
        outgoing.Parameter.Should().Be(Token);
    }

    [Theory]
    [InlineData("http://localhost:5000/grpc")]
    [InlineData("http://127.0.0.1:5000/grpc")]
    [InlineData("http://[::1]:5000/grpc")]
    public async Task should_forward_bearer_token_to_plaintext_loopback_destination(string destination)
    {
        // Act
        var outgoing = await SendThroughHandler($"Bearer {Token}", destination);

        // Assert
        outgoing.Should().NotBeNull();
        outgoing!.Scheme.Should().Be("Bearer");
        outgoing.Parameter.Should().Be(Token);
    }

    [Theory]
    [InlineData("http://flight.internal/grpc")]
    [InlineData("http://10.0.0.5:5000/grpc")]
    [InlineData("http://attacker.example.com/grpc")]
    public async Task should_not_forward_bearer_token_to_plaintext_non_loopback_destination(string destination)
    {
        // Act
        var outgoing = await SendThroughHandler($"Bearer {Token}", destination);

        // Assert
        outgoing.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_not_set_authorization_when_incoming_request_has_no_token(string? incomingAuthorization)
    {
        // Act
        var outgoing = await SendThroughHandler(incomingAuthorization, "https://flight.internal/grpc");

        // Assert
        outgoing.Should().BeNull();
    }

    [Fact]
    public async Task should_not_set_authorization_when_there_is_no_http_context()
    {
        // Arrange
        var capturing = new CapturingHandler();
        var handler = new AuthHeaderHandler(new HttpContextAccessor { HttpContext = null })
        {
            InnerHandler = capturing,
        };

        // Act
        using var client = new HttpClient(handler);
        await client.GetAsync("https://flight.internal/grpc");

        // Assert
        capturing.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task should_leave_existing_authorization_untouched_for_untrusted_destination()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {Token}";

        var capturing = new CapturingHandler();
        var handler = new AuthHeaderHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = capturing,
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://flight.internal/grpc");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "preset-token");

        // Act
        using var client = new HttpClient(handler);
        await client.SendAsync(request);

        // Assert
        capturing.Authorization.Should().NotBeNull();
        capturing.Authorization!.Parameter.Should().Be("preset-token");
    }

    private static async Task<AuthenticationHeaderValue?> SendThroughHandler(
        string? incomingAuthorization,
        string destination
    )
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
        await client.GetAsync(destination);

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
