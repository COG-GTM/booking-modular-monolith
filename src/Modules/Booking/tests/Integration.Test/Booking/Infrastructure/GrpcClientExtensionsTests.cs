using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Booking.Extensions.Infrastructure;
using BookingFlight;
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

namespace Integration.Test.Booking.Infrastructure;

// Docker-free: exercises AddGrpcClients wiring against a stubbed HTTP handler, no TestServer or containers.
public class GrpcClientExtensionsTests
{
    private const string CallerToken = "caller-access-token";

    [Fact]
    public async Task should_forward_caller_bearer_token_on_passenger_grpc_client()
    {
        var stub = new RecordingHandler();
        await using var provider = BuildProvider(stub, authorizationHeader: $"Bearer {CallerToken}");
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        var act = async () =>
            await client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.CreateVersion7().ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented);
        stub.Requests.Should().ContainSingle();
        stub.Requests[0].RequestUri!.Host.Should().Be("passenger.test");
        stub.Requests[0].Headers.Authorization.Should().NotBeNull();
        stub.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        stub.Requests[0].Headers.Authorization!.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task should_not_add_bearer_token_on_flight_grpc_client()
    {
        var stub = new RecordingHandler();
        await using var provider = BuildProvider(stub, authorizationHeader: $"Bearer {CallerToken}");
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.CreateVersion7().ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented);
        stub.Requests.Should().ContainSingle();
        stub.Requests[0].RequestUri!.Host.Should().Be("flight.test");
        stub.Requests[0].Headers.Authorization.Should().BeNull();
    }

    private static ServiceProvider BuildProvider(RecordingHandler stub, string authorizationHeader)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Grpc:FlightAddress"] = "http://flight.test",
                    ["Grpc:PassengerAddress"] = "http://passenger.test",
                }
            )
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = authorizationHeader;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(accessor);
        services.AddTransient<AuthHeaderHandler>();
        services.AddGrpcClients();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = stub)
        );

        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);

            // Trailers-only gRPC response: the status travels in the headers and no message body follows.
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", ((int)StatusCode.Unimplemented).ToString());

            return Task.FromResult(response);
        }
    }
}
