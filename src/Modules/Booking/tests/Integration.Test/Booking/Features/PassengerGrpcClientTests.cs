using System.Net;
using Booking.Extensions.Infrastructure;
using BookingPassenger;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Integration.Test.Booking.Features;

public class PassengerGrpcClientTests
{
    [Fact]
    public async Task should_forward_the_incoming_bearer_token_to_passenger_grpc()
    {
        const string bearer = "Bearer test-token";
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = bearer;
        var transport = new RecordingTransport();

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
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        services.AddTransient<AuthHeaderHandler>();
        services.AddGrpcClients();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = transport)
        );

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync
        );

        exception.StatusCode.Should().Be(StatusCode.NotFound);
        transport.Authorization.Should().Be(bearer);
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization?.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new("application/grpc");
            response.Headers.Add("grpc-status", "5"); // NotFound
            return Task.FromResult(response);
        }
    }
}
