using System.Net;
using System.Net.Http.Headers;
using Booking.Extensions.Infrastructure;
using BookingFlight;
using BuildingBlocks.Jwt;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Integration.Test;

public class FlightGrpcClientAuthorizationTests
{
    [Theory]
    [InlineData("first-token")]
    [InlineData("second-token")]
    public async Task should_forward_current_bearer_token_through_registered_flight_client(string token)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string>
                    {
                        ["Grpc:FlightAddress"] = "https://flight.test",
                        ["Grpc:PassengerAddress"] = "https://passenger.test",
                    }
                )
                .Build()
        );
        services.AddHttpContextAccessor();
        services.AddTransient<AuthHeaderHandler>();
        services.AddGrpcClients();
        using var handler = new RecordingHandler();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler)
        );
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Request.Headers.Authorization = $"Bearer {token}";
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        try
        {
            var exception = await Assert.ThrowsAsync<RpcException>(
                () => client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync
            );

            Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
            Assert.Equal("Bearer", handler.Authorization?.Scheme);
            Assert.Equal(token, handler.Authorization?.Parameter);
            Assert.Equal("/bookingFlight.FlightGrpcService/GetById", handler.RequestPath);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue Authorization { get; private set; }
        public string RequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization;
            RequestPath = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", "7");
            return Task.FromResult(response);
        }
    }
}
