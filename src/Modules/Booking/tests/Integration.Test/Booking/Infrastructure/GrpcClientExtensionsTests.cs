using System;
using System.Collections.Generic;
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
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class GrpcClientExtensionsTests
{
    private const string IncomingToken = "incoming-user-token";

    [Fact]
    public async Task flight_grpc_client_should_forward_the_callers_bearer_token_over_https()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(
            handler,
            $"Bearer {IncomingToken}",
            flightAddress: "https://flight.test"
        );
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        await act.Should().ThrowAsync<RpcException>();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().StartWith("https://flight.test");
        handler.LastRequest.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(IncomingToken);
    }

    [Fact]
    public async Task passenger_grpc_client_should_forward_the_callers_bearer_token_over_https()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(
            handler,
            $"Bearer {IncomingToken}",
            passengerAddress: "https://passenger.test"
        );
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        await act.Should().ThrowAsync<RpcException>();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().StartWith("https://passenger.test");
        handler.LastRequest.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(IncomingToken);
    }

    [Fact]
    public async Task flight_grpc_client_should_forward_the_callers_bearer_token_to_plaintext_loopback()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(
            handler,
            $"Bearer {IncomingToken}",
            flightAddress: "http://localhost:5001"
        );
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        await act.Should().ThrowAsync<RpcException>();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(IncomingToken);
    }

    [Fact]
    public async Task flight_grpc_client_should_not_forward_the_callers_bearer_token_to_plaintext_external_address()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(
            handler,
            $"Bearer {IncomingToken}",
            flightAddress: "http://flight.internal:5001"
        );
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task passenger_grpc_client_should_not_forward_the_callers_bearer_token_to_plaintext_external_address()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(
            handler,
            $"Bearer {IncomingToken}",
            passengerAddress: "http://passenger.internal:5002"
        );
        var client = provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task grpc_clients_should_not_send_authorization_when_there_is_no_incoming_token()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(handler, incomingAuthorizationHeader: null);
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        await act.Should().ThrowAsync<RpcException>();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task flight_grpc_client_should_surface_unauthenticated_status_from_the_server()
    {
        // Arrange
        var handler = new RecordingHandler();
        await using var provider = BuildServiceProvider(handler, $"Bearer {IncomingToken}");
        var client = provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        // Act
        var act = async () =>
            await client.GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    private static ServiceProvider BuildServiceProvider(
        HttpMessageHandler primaryHandler,
        string? incomingAuthorizationHeader,
        string flightAddress = "https://flight.test",
        string passengerAddress = "https://passenger.test"
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    { "Grpc:FlightAddress", flightAddress },
                    { "Grpc:PassengerAddress", passengerAddress },
                }
            )
            .Build();

        var httpContext = new DefaultHttpContext();
        if (incomingAuthorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = incomingAuthorizationHeader;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        services.AddTransient<AuthHeaderHandler>();

        services.AddGrpcClients();

        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = primaryHandler)
        );

        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
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
                Version = HttpVersion.Version20,
                RequestMessage = request,
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", ((int)StatusCode.Unauthenticated).ToString());

            return Task.FromResult(response);
        }
    }
}
