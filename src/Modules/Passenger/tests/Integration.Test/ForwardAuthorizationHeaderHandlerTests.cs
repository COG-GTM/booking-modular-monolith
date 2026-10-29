using System.Net;
using System.Net.Http.Headers;
using Booking.Extensions.Infrastructure;
using BookingPassenger;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Integration.Test;

public class ForwardAuthorizationHeaderHandlerTests
{
    [Theory]
    [InlineData("Bearer caller-token", null, "Bearer caller-token")]
    [InlineData("Bearer caller-token", "Bearer explicit-token", "Bearer explicit-token")]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("  ", null, null)]
    [InlineData(null, "Bearer explicit-token", "Bearer explicit-token")]
    public async Task should_forward_only_when_incoming_authorization_is_present_and_outgoing_is_unset(
        string? incoming,
        string? outgoing,
        string? expected
    )
    {
        var context = new DefaultHttpContext();
        if (incoming is not null)
        {
            context.Request.Headers.Authorization = incoming;
        }

        using var terminal = new RecordingHandler();
        using var handler = new ForwardAuthorizationHeaderHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = terminal,
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://passenger.test/passenger");
        if (outgoing is not null)
        {
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(outgoing);
        }

        using var cts = new CancellationTokenSource();
        using var response = await invoker.SendAsync(request, cts.Token);

        Assert.Equal(expected, terminal.Authorization);
        Assert.Equal(cts.Token, terminal.CancellationToken);
        Assert.Same(request, terminal.Request);
    }

    [Fact]
    public async Task should_allow_calls_without_an_http_context()
    {
        using var terminal = new RecordingHandler();
        using var handler = new ForwardAuthorizationHeaderHandler(new HttpContextAccessor())
        {
            InnerHandler = terminal,
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://passenger.test/passenger");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Null(terminal.Authorization);
        Assert.Same(request, terminal.Request);
    }

    [Fact]
    public async Task should_forward_each_callers_token_through_the_registered_passenger_grpc_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Grpc:FlightAddress"] = "https://flight.test",
                        ["Grpc:PassengerAddress"] = "https://passenger.test",
                    }
                )
                .Build()
        );
        services.AddHttpContextAccessor();
        using var terminal = new RecordingHandler();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = terminal)
        );
        services.AddGrpcClients();
        await using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        foreach (var token in new[] { "first-caller", "second-caller" })
        {
            accessor.HttpContext = new DefaultHttpContext();
            accessor.HttpContext.Request.Headers.Authorization = $"Bearer {token}";

            using var call = client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });
            var exception = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

            Assert.Equal(StatusCode.NotFound, exception.StatusCode);
            Assert.Equal($"Bearer {token}", terminal.Authorization);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization?.ToString();
            CancellationToken = cancellationToken;
            Request = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", "5");
            return Task.FromResult(response);
        }
    }
}
