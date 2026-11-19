using System.Net;
using System.Security.Claims;
using Api;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit;

namespace Integration.Test.Seat.Features;

public class FlightGrpcAuthorizationTests : FlightIntegrationTestBase
{
    public FlightGrpcAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Theory]
    [InlineData(null)]
    [InlineData("unrelated-api")]
    public async Task should_deny_reserve_seat_without_the_required_scope(string? scope)
    {
        using var httpClient = Fixture.HttpClient;
        var claims = new Dictionary<string, object> { [ClaimTypes.Name] = "test@sample.com" };
        if (scope is not null)
        {
            claims["scope"] = scope;
        }

        httpClient.SetFakeBearerToken(claims);
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient }
        );
        var grpcClient = new FlightGrpcService.FlightGrpcServiceClient(channel);

        var act = async () =>
            await grpcClient.ReserveSeatAsync(
                new ReserveSeatRequest { FlightId = Guid.NewGuid().ToString(), SeatNumber = "12C" }
            );

        await act.Should()
            .ThrowAsync<RpcException>()
            .Where(exception => exception.StatusCode == StatusCode.PermissionDenied);
    }
}
