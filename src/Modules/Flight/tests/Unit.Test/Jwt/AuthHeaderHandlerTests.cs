using System.Net;
using System.Net.Http.Headers;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test.Jwt;

public class AuthHeaderHandlerTests
{
    [Theory]
    [InlineData("Bearer abc.def.ghi")]
    [InlineData("bearer abc.def.ghi")]
    [InlineData("BEARER abc.def.ghi")]
    [InlineData("abc.def.ghi")]
    [InlineData("Bearer   abc.def.ghi  ")]
    public async Task should_forward_incoming_token_as_bearer_authorization(string incomingHeader)
    {
        var outgoing = await SendThroughHandler(CreateAccessor(incomingHeader));

        outgoing.Should().NotBeNull();
        outgoing!.Scheme.Should().Be("Bearer");
        outgoing.Parameter.Should().Be("abc.def.ghi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("   ")]
    public async Task should_not_set_authorization_when_incoming_request_has_no_token(string incomingHeader)
    {
        var outgoing = await SendThroughHandler(CreateAccessor(incomingHeader));

        outgoing.Should().BeNull();
    }

    [Fact]
    public async Task should_not_set_authorization_when_there_is_no_http_context()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);

        var outgoing = await SendThroughHandler(accessor);

        outgoing.Should().BeNull();
    }

    [Fact]
    public async Task should_not_set_authorization_when_accessor_is_null()
    {
        var outgoing = await SendThroughHandler(null);

        outgoing.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_existing_outgoing_authorization_when_incoming_request_has_no_token()
    {
        var existing = new AuthenticationHeaderValue("Bearer", "service-token");

        var outgoing = await SendThroughHandler(CreateAccessor(null), existing);

        outgoing.Should().Be(existing);
    }

    private static IHttpContextAccessor CreateAccessor(string incomingHeader)
    {
        var httpContext = new DefaultHttpContext();

        if (incomingHeader != null)
        {
            httpContext.Request.Headers.Authorization = incomingHeader;
        }

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private static async Task<AuthenticationHeaderValue> SendThroughHandler(
        IHttpContextAccessor accessor,
        AuthenticationHeaderValue existing = null
    )
    {
        var recorder = new RecordingHandler();
        using var invoker = new HttpMessageInvoker(new AuthHeaderHandler(accessor) { InnerHandler = recorder });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "http://localhost/flight.FlightGrpcService/GetById"
        );

        if (existing != null)
        {
            request.Headers.Authorization = existing;
        }

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return recorder.Authorization;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue Authorization { get; private set; }

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
