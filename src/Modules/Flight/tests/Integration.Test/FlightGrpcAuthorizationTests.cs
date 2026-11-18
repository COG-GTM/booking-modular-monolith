using System.Net;
using Api;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit;

namespace Integration.Test;

public class FlightGrpcAuthorizationTests : FlightIntegrationTestBase
{
    public FlightGrpcAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData("GetById", "booking-api")]
    [InlineData("GetAvailableSeats", "booking-api")]
    [InlineData("ReserveSeat", "booking-api")]
    [InlineData("GetById", null)]
    [InlineData("GetAvailableSeats", null)]
    [InlineData("ReserveSeat", null)]
    public async Task should_deny_authenticated_callers_without_flight_scope(string method, string scope)
    {
        using var httpClient = Fixture.HttpClient;
        var claims = new Dictionary<string, object> { { "sub", "test-user" } };
        if (scope != null)
            claims.Add("scope", scope);
        httpClient.SetFakeBearerToken(claims);
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient }
        );

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(channel, method));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Theory]
    [InlineData("GetById")]
    [InlineData("GetAvailableSeats")]
    public async Task should_reject_anonymous_callers(string method)
    {
        using var channel = Fixture.AnonymousChannel;

        var exception = await Assert.ThrowsAsync<RpcException>(() => CallAsync(channel, method));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    private static async Task CallAsync(GrpcChannel channel, string method)
    {
        var client = new FlightGrpcService.FlightGrpcServiceClient(channel);
        var id = Guid.NewGuid().ToString();
        switch (method)
        {
            case "GetById":
                await client.GetByIdAsync(new GetByIdRequest { Id = id });
                break;
            case "GetAvailableSeats":
                await client.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = id });
                break;
            case "ReserveSeat":
                await client.ReserveSeatAsync(new ReserveSeatRequest { FlightId = id, SeatNumber = "1A" });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }
    }
}
