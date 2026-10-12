using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Booking.Extensions.Infrastructure;
using BookingPassenger;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test.Extensions.Infrastructure;

public class GrpcClientExtensionsTests
{
    private const string FlightAddress = "http://flight.test";
    private const string PassengerAddress = "http://passenger.test";

    [Fact]
    public async Task passenger_grpc_client_should_forward_incoming_bearer_token_to_passenger_service()
    {
        // Arrange
        var passengerService = new FakeGrpcServerHandler(StatusCode.PermissionDenied);
        await using var provider = BuildServiceProvider(passengerService, "Bearer incoming-token");
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        // Act
        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);

        var request = passengerService.Requests.Should().ContainSingle().Subject;
        request.RequestUri?.GetLeftPart(UriPartial.Authority).Should().Be(PassengerAddress);
        request.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "incoming-token"));
    }

    [Fact]
    public async Task passenger_grpc_client_should_send_bearer_scheme_when_incoming_request_has_no_token()
    {
        // Arrange
        var passengerService = new FakeGrpcServerHandler(StatusCode.Unauthenticated);
        await using var provider = BuildServiceProvider(passengerService, authorizationHeader: null);
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        // Act
        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);

        var request = passengerService.Requests.Should().ContainSingle().Subject;
        request.Headers.Authorization?.Scheme.Should().Be("Bearer");
        request.Headers.Authorization?.Parameter.Should().BeNullOrEmpty();
    }

    private static ServiceProvider BuildServiceProvider(HttpMessageHandler primaryHandler, string? authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    { "Grpc:FlightAddress", FlightAddress },
                    { "Grpc:PassengerAddress", PassengerAddress },
                })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(httpContextAccessor);
        // mirrors SharedInfrastructureExtensions, which registers the handler for the whole host
        services.AddTransient<AuthHeaderHandler>();

        services.AddGrpcClients();

        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = primaryHandler));

        return services.BuildServiceProvider();
    }

    private sealed class FakeGrpcServerHandler : HttpMessageHandler
    {
        private readonly StatusCode _statusCode;

        public FakeGrpcServerHandler(StatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            // gRPC "trailers-only" response: the status travels in the response headers
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                RequestMessage = request,
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.TryAddWithoutValidation("grpc-status", ((int)_statusCode).ToString(CultureInfo.InvariantCulture));

            return Task.FromResult(response);
        }
    }
}
