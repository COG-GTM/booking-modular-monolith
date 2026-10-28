using System.Net;
using System.Net.Http.Headers;
using Booking.Extensions.Infrastructure;
using Contracts.Grpc.Flight;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PassengerGrpcService = Contracts.Grpc.Passenger.PassengerGrpcService;

namespace Booking.Unit.Test;

public class GrpcReadRetryTests
{
    [Theory]
    [InlineData("flight.FlightGrpcService/GetAvailableSeats", 1)]
    [InlineData("flight.FlightGrpcService/GetAvailableSeats", 3)]
    [InlineData("passenger.PassengerGrpcService/GetById", 1)]
    [InlineData("passenger.PassengerGrpcService/GetById", 5)]
    public async Task unavailable_reads_stop_at_the_configured_total_attempts(string rpc, int attempts)
    {
        using var handler = new GrpcResponseHandler(StatusCode.Unavailable);
        using var provider = CreateProvider(handler, attempts);

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(provider, rpc));

        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        handler.Calls.Should().Be(attempts);
        handler.RequestUri!.AbsolutePath.Should().Be("/" + rpc);
    }

    [Theory]
    [InlineData("flight.FlightGrpcService/GetAvailableSeats")]
    [InlineData("passenger.PassengerGrpcService/GetById")]
    public async Task unavailable_reads_recover_when_the_next_attempt_succeeds(string rpc)
    {
        using var handler = new GrpcResponseHandler(StatusCode.Unavailable, succeedAfter: 2);
        using var provider = CreateProvider(handler, 3);

        await CallAsync(provider, rpc);

        handler.Calls.Should().Be(3);
    }

    [Theory]
    [InlineData("flight.FlightGrpcService/GetAvailableSeats", StatusCode.NotFound)]
    [InlineData("flight.FlightGrpcService/GetAvailableSeats", StatusCode.InvalidArgument)]
    [InlineData("passenger.PassengerGrpcService/GetById", StatusCode.NotFound)]
    [InlineData("passenger.PassengerGrpcService/GetById", StatusCode.PermissionDenied)]
    public async Task non_transient_grpc_errors_are_not_retried(string rpc, StatusCode status)
    {
        using var handler = new GrpcResponseHandler(status);
        using var provider = CreateProvider(handler, 3);

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(provider, rpc));

        exception.StatusCode.Should().Be(status);
        handler.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("flight.FlightGrpcService/GetAvailableSeats", "flight", 5081)]
    [InlineData("passenger.PassengerGrpcService/GetById", "passenger", 5082)]
    public async Task aspire_service_names_resolve_using_configured_endpoints(string rpc, string service, int port)
    {
        using var handler = new GrpcResponseHandler(StatusCode.OK, succeedAfter: 0);
        using var provider = CreateProvider(
            handler,
            1,
            new Dictionary<string, string?>
            {
                ["Grpc:FlightAddress"] = "http://flight",
                ["Grpc:PassengerAddress"] = "http://passenger",
                [$"Services:{service}:http:0"] = $"http://127.0.0.1:{port}",
            }
        );

        await CallAsync(provider, rpc);

        handler.RequestUri.Should().Be(new Uri($"http://127.0.0.1:{port}/{rpc}"));
    }

    private static async Task CallAsync(IServiceProvider provider, string rpc)
    {
        if (rpc == "flight.FlightGrpcService/GetAvailableSeats")
        {
            await provider
                .GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>()
                .GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = "flight" });
        }
        else
        {
            await provider
                .GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
                .GetByIdAsync(new Contracts.Grpc.Passenger.GetByIdRequest { Id = "passenger" });
        }
    }

    private static ServiceProvider CreateProvider(
        HttpMessageHandler handler,
        int attempts,
        Dictionary<string, string?>? overrides = null
    )
    {
        var settings = new Dictionary<string, string?>
        {
            ["Grpc:FlightAddress"] = "http://flight-service:81",
            ["Grpc:PassengerAddress"] = "http://passenger-service:82",
            ["Grpc:MaxRetryAttempts"] = attempts.ToString(),
            ["Grpc:InitialBackoff"] = "00:00:00.001",
            ["Grpc:MaxBackoff"] = "00:00:00.005",
        };
        foreach (var setting in overrides ?? [])
        {
            settings[setting.Key] = setting.Value;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => handler));
        services.AddGrpcClients(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class GrpcResponseHandler(StatusCode status, int succeedAfter = int.MaxValue) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            RequestUri = request.RequestUri;
            var success = Calls > succeedAfter;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(success ? new byte[5] : []),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            if (success)
            {
                response.TrailingHeaders.Add("grpc-status", "0");
            }
            else
            {
                response.Headers.Add(
                    "grpc-status",
                    ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
            }
            return Task.FromResult(response);
        }
    }
}
