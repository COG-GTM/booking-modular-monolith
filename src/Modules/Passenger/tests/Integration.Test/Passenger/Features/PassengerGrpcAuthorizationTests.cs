using System.Net;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class PassengerGrpcAuthorizationTests : PassengerIntegrationTestBase
{
    public PassengerGrpcAuthorizationTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData(null)]
    [InlineData("unrelated-api")]
    public async Task should_deny_authenticated_passenger_grpc_calls_without_the_required_scope(string? scope)
    {
        using var httpClient = Fixture.CreateAnonymousHttpClient();
        var claims = new Dictionary<string, object> { ["sub"] = "passenger-test-user" };
        if (scope is not null)
        {
            claims["scope"] = scope;
        }

        httpClient.SetFakeBearerToken(claims);
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient }
        );
        var client = new PassengerGrpcService.PassengerGrpcServiceClient(channel);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync
        );

        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }
}
