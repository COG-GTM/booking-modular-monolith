using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Booking.Extensions.Infrastructure;
using BookingFlight;
using BookingPassenger;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NSubstitute;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class ForwardAuthorizationHeaderHandlerTests
{
    private const string Token = "Bearer caller-token";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = new Version(2, 0),
                RequestMessage = request,
            };
            response.Content = new ByteArrayContent(Array.Empty<byte>());
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/grpc");
            response.Headers.Add("grpc-status", ((int)StatusCode.NotFound).ToString());
            return Task.FromResult(response);
        }
    }

    private static IHttpContextAccessor AccessorWith(string authorization)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        if (authorization is not null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    private static async Task<HttpRequestMessage> SendThroughHandler(
        IHttpContextAccessor accessor,
        HttpRequestMessage request
    )
    {
        var recorder = new RecordingHandler();
        using var invoker = new HttpMessageInvoker(
            new ForwardAuthorizationHeaderHandler(accessor) { InnerHandler = recorder }
        );

        await invoker.SendAsync(request, CancellationToken.None);

        return recorder.Requests.Single();
    }

    [Fact]
    public async Task should_forward_incoming_authorization_header()
    {
        var sent = await SendThroughHandler(
            AccessorWith(Token),
            new HttpRequestMessage(HttpMethod.Post, "http://passenger")
        );

        sent.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be(Token);
    }

    [Fact]
    public async Task should_not_overwrite_authorization_header_already_on_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://passenger");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer explicit");

        var sent = await SendThroughHandler(AccessorWith(Token), request);

        sent.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer explicit");
    }

    [Fact]
    public async Task should_not_add_header_when_incoming_request_has_none()
    {
        var sent = await SendThroughHandler(
            AccessorWith(null),
            new HttpRequestMessage(HttpMethod.Post, "http://passenger")
        );

        sent.Headers.Contains("Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task should_not_add_header_outside_of_http_request()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);

        var sent = await SendThroughHandler(accessor, new HttpRequestMessage(HttpMethod.Post, "http://passenger"));

        sent.Headers.Contains("Authorization").Should().BeFalse();
    }

    private static (ServiceProvider Provider, RecordingHandler Recorder) BuildGrpcClients()
    {
        var recorder = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string>
                    {
                        ["Grpc:FlightAddress"] = "http://flight.test",
                        ["Grpc:PassengerAddress"] = "http://passenger.test",
                    }
                )
                .Build()
        );
        services.AddLogging();
        services.AddSingleton(AccessorWith(Token));
        services.AddGrpcClients();
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = recorder)
        );

        return (services.BuildServiceProvider(), recorder);
    }

    [Fact]
    public async Task passenger_grpc_client_should_forward_caller_authorization()
    {
        var (provider, recorder) = BuildGrpcClients();
        await using var _ = provider;
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        var act = async () =>
            await client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        await act.Should().ThrowAsync<RpcException>();
        var request = recorder.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.Host.Should().Be("passenger.test");
        request.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be(Token);
    }

    [Fact]
    public async Task flight_grpc_client_should_not_forward_caller_authorization()
    {
        var (provider, recorder) = BuildGrpcClients();
        await using var _ = provider;
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        await act.Should().ThrowAsync<RpcException>();
        var request = recorder.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.Host.Should().Be("flight.test");
        request.Headers.Contains("Authorization").Should().BeFalse();
    }
}
