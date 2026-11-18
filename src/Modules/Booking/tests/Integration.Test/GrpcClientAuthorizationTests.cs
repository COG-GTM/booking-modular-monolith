using System.Net;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Jwt;
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
    [InlineData(true, "Bearer caller-token", "Bearer caller-token")]
    [InlineData(false, "Bearer caller-token", "Bearer caller-token")]
    [InlineData(true, null, null)]
    [InlineData(false, null, null)]
    [InlineData(true, "bearer caller-token", "Bearer caller-token")]
    [InlineData(false, "bearer caller-token", "Bearer caller-token")]
    public async Task should_forward_only_the_callers_authorization_header(
        bool flight,
        string? authorization,
        string? expectedAuthorization
    )
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        var handler = new RecordingHandler();
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
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler)
        );
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            if (flight)
            {
                using var call = provider
                    .GetRequiredService<BookingFlight.FlightGrpcService.FlightGrpcServiceClient>()
                    .GetByIdAsync(new BookingFlight.GetByIdRequest { Id = Guid.NewGuid().ToString() });
                await call.ResponseAsync;
            }
            else
            {
                using var call = provider
                    .GetRequiredService<BookingPassenger.PassengerGrpcService.PassengerGrpcServiceClient>()
                    .GetByIdAsync(new BookingPassenger.GetByIdRequest { Id = Guid.NewGuid().ToString() });
                await call.ResponseAsync;
            }
        });

        exception.StatusCode.Should().Be(StatusCode.NotFound);
        handler.RequestUri!.Host.Should().Be(flight ? "flight.test" : "passenger.test");
        handler.Authorization.Should().Be(expectedAuthorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization?.ToString();
            RequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new("application/grpc");
            response.Headers.Add("grpc-status", "5");
            return Task.FromResult(response);
        }
    }
}
