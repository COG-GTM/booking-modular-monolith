using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
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

public class GrpcClientAuthHeaderTests
{
    private const string FlightAddress = "http://flight.test:5001";
    private const string PassengerAddress = "http://passenger.test:5002";

    [Fact]
    public async Task flight_grpc_client_should_forward_incoming_bearer_token()
    {
        var recorder = new RecordingGrpcHandler();
        using var provider = BuildProvider(recorder, "Bearer user-token");

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        await CallIgnoringStatus(
            () => client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = "1" }).ResponseAsync
        );

        recorder.RequestUri!.Host.Should().Be("flight.test");
        recorder.Authorization.Should().BeEquivalentTo(new AuthenticationHeaderValue("Bearer", "user-token"));
    }

    [Fact]
    public async Task passenger_grpc_client_should_forward_incoming_bearer_token()
    {
        var recorder = new RecordingGrpcHandler();
        using var provider = BuildProvider(recorder, "Bearer user-token");

        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();
        await CallIgnoringStatus(
            () => client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = "1" }).ResponseAsync
        );

        recorder.RequestUri!.Host.Should().Be("passenger.test");
        recorder.Authorization.Should().BeEquivalentTo(new AuthenticationHeaderValue("Bearer", "user-token"));
    }

    [Fact]
    public async Task flight_grpc_client_should_not_send_authorization_without_incoming_token()
    {
        var recorder = new RecordingGrpcHandler();
        using var provider = BuildProvider(recorder, null);

        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
        await CallIgnoringStatus(
            () => client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = "1" }).ResponseAsync
        );

        recorder.RequestUri.Should().NotBeNull();
        recorder.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task passenger_grpc_client_should_not_send_authorization_without_incoming_token()
    {
        var recorder = new RecordingGrpcHandler();
        using var provider = BuildProvider(recorder, null);

        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();
        await CallIgnoringStatus(
            () => client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = "1" }).ResponseAsync
        );

        recorder.RequestUri.Should().NotBeNull();
        recorder.Authorization.Should().BeNull();
    }

    private static ServiceProvider BuildProvider(RecordingGrpcHandler recorder, string incomingAuthorization)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Grpc:FlightAddress"] = FlightAddress,
                    ["Grpc:PassengerAddress"] = PassengerAddress,
                }
            )
            .Build();

        var httpContext = new DefaultHttpContext();
        if (incomingAuthorization != null)
        {
            httpContext.Request.Headers.Authorization = incomingAuthorization;
        }

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton(accessor);
        services.AddTransient<AuthHeaderHandler>();
        services.AddGrpcClients();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = recorder)
        );

        return services.BuildServiceProvider();
    }

    private static async Task CallIgnoringStatus(Func<Task> call)
    {
        var act = call;
        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented);
    }

    private sealed class RecordingGrpcHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue Authorization { get; private set; }
        public Uri RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization;
            RequestUri = request.RequestUri;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent([]),
                RequestMessage = request,
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", ((int)StatusCode.Unimplemented).ToString());

            return Task.FromResult(response);
        }
    }
}
