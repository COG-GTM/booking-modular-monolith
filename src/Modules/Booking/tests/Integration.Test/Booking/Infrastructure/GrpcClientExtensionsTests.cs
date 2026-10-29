using System.Net;
using System.Net.Http.Headers;
using Booking.Extensions.Infrastructure;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class GrpcClientExtensionsTests
{
    private const string Token = "inbound-access-token";

    [Fact]
    public async Task flight_grpc_client_should_forward_the_inbound_bearer_token()
    {
        var handler = new RecordingGrpcHandler();
        await using var provider = BuildProvider(handler, $"Bearer {Token}");

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        await client.ReserveSeatAsync(
            new ReserveSeatRequest { FlightId = Guid.NewGuid().ToString(), SeatNumber = "12C" }
        );

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be("https://flight.test/bookingFlight.FlightGrpcService/ReserveSeat");
        handler.Requests[0].Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", Token));
    }

    [Fact]
    public async Task passenger_grpc_client_should_forward_the_inbound_bearer_token()
    {
        var handler = new RecordingGrpcHandler();
        await using var provider = BuildProvider(handler, $"Bearer {Token}");

        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();
        await client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().StartWith("https://passenger.test/");
        handler.Requests[0].Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", Token));
    }

    [Fact]
    public async Task grpc_clients_should_forward_the_token_of_the_current_request()
    {
        var handler = new RecordingGrpcHandler();
        await using var provider = BuildProvider(handler, "Bearer first-token");
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });
        accessor.HttpContext = CreateHttpContext("Bearer second-token");
        await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        handler
            .Requests.Select(request => request.Authorization?.Parameter)
            .Should()
            .Equal("first-token", "second-token");
    }

    [Fact]
    public async Task grpc_clients_should_not_send_a_token_when_there_is_no_inbound_authorization()
    {
        var handler = new RecordingGrpcHandler();
        await using var provider = BuildProvider(handler, authorization: null);

        await provider
            .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
            .GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });
        await provider
            .GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
            .GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        handler.Requests.Should().HaveCount(2);
        handler
            .Requests.Select(request => request.Authorization?.Parameter)
            .Should()
            .AllSatisfy(token => token.Should().BeNullOrEmpty());
    }

    private static ServiceProvider BuildProvider(RecordingGrpcHandler handler, string? authorization)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:FlightAddress"] = "https://flight.test",
                    ["Grpc:PassengerAddress"] = "https://passenger.test",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddTransient<AuthHeaderHandler>();
        services.AddGrpcClients();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler)
        );

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = authorization is null
            ? null
            : CreateHttpContext(authorization);

        return provider;
    }

    private static DefaultHttpContext CreateHttpContext(string authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorization;
        return context;
    }

    private sealed record RecordedRequest(string Uri, AuthenticationHeaderValue? Authorization);

    private sealed class RecordingGrpcHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(new RecordedRequest(request.RequestUri!.ToString(), request.Headers.Authorization));

            // An empty, uncompressed gRPC message frame followed by an OK status.
            var content = new ByteArrayContent(new byte[5]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = content,
                RequestMessage = request,
            };
            response.TrailingHeaders.Add("grpc-status", "0");

            return Task.FromResult(response);
        }
    }
}
