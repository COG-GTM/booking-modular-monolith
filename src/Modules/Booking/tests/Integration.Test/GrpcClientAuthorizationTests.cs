using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Booking.Extensions.Infrastructure;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Integration.Test;

public class GrpcClientAuthorizationTests
{
    [Theory]
    [InlineData(true, "Bearer token-123")]
    [InlineData(false, "Bearer token-123")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public async Task should_propagate_incoming_authorization_to_grpc_client(bool flight, string authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;
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
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        services.AddGrpcClients();
        var handler = new RecordingHandler();
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler)
        );
        using var provider = services.BuildServiceProvider();

        Func<Task> call = flight
            ? async () =>
                await provider
                    .GetRequiredService<BookingFlight.FlightGrpcService.FlightGrpcServiceClient>()
                    .GetByIdAsync(new BookingFlight.GetByIdRequest { Id = "1" })
                    .ResponseAsync
            : async () =>
                await provider
                    .GetRequiredService<BookingPassenger.PassengerGrpcService.PassengerGrpcServiceClient>()
                    .GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = "1" })
                    .ResponseAsync;

        await call.Should().ThrowAsync<RpcException>().Where(exception => exception.StatusCode == StatusCode.NotFound);
        handler.Request.Should().NotBeNull();
        handler.Request.RequestUri.Host.Should().Be(flight ? "flight.test" : "passenger.test");
        if (authorization is null)
            handler.Request.Headers.Contains("Authorization").Should().BeFalse();
        else
            handler.Request.Headers.Authorization.ToString().Should().Be(authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentType = new("application/grpc");
            response.Headers.Add("grpc-status", "5");
            return Task.FromResult(response);
        }
    }
}
