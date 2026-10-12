using System.Net;
using System.Net.Http.Headers;
using Booking.Extensions.Infrastructure;
using BookingFlight;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class GrpcClientExtensionsTests
{
    [Theory]
    [InlineData("Bearer caller-token")]
    [InlineData("bearer caller-token")]
    public async Task flight_grpc_client_should_forward_bearer_token_of_current_request(string incomingAuthorization)
    {
        // Arrange
        var capturingHandler = new CapturingHandler();
        await using var provider = BuildServiceProvider(capturingHandler, incomingAuthorization);
        var flightGrpcClient = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        // Act
        var act = async () =>
            await flightGrpcClient.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        await act.Should().ThrowAsync<RpcException>();

        capturingHandler.LastRequest.Should().NotBeNull();
        capturingHandler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        capturingHandler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturingHandler.LastRequest.Headers.Authorization.Parameter.Should().Be("caller-token");
    }

    private static ServiceProvider BuildServiceProvider(HttpMessageHandler primaryHandler, string authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = authorizationHeader;

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:FlightAddress"] = "http://flight.test",
                    ["Grpc:PassengerAddress"] = "http://passenger.test",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(httpContextAccessor);
        services.AddTransient<AuthHeaderHandler>();

        services.AddGrpcClients();

        services
            .AddHttpClient(nameof(FlightGrpcService.FlightGrpcServiceClient))
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        return services.BuildServiceProvider();
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

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", ((int)StatusCode.Unimplemented).ToString());

            return Task.FromResult(response);
        }
    }
}
