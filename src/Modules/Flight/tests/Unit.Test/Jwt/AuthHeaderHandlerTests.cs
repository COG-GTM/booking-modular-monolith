namespace Unit.Test.Jwt
{
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using global::BuildingBlocks.Jwt;
    using Microsoft.AspNetCore.Http;
    using Xunit;

    public class AuthHeaderHandlerTests
    {
        [Theory]
        [InlineData("Bearer abc", "Bearer abc")]
        [InlineData("bearer abc", "Bearer abc")]
        [InlineData(null, null)]
        [InlineData("Basic xyz", null)]
        public async Task should_forward_only_valid_bearer_authorization(
            string? incomingAuthorization,
            string? expectedAuthorization
        )
        {
            var context = new DefaultHttpContext();
            if (incomingAuthorization is not null)
            {
                context.Request.Headers.Authorization = incomingAuthorization;
            }

            var innerHandler = new CaptureAuthorizationHandler();
            var handler = new AuthHeaderHandler(new HttpContextAccessor { HttpContext = context })
            {
                InnerHandler = innerHandler,
            };
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost");

            using var response = await client.SendAsync(request);

            Assert.Equal(expectedAuthorization, innerHandler.Authorization?.ToString());
        }

        private sealed class CaptureAuthorizationHandler : HttpMessageHandler
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
}
