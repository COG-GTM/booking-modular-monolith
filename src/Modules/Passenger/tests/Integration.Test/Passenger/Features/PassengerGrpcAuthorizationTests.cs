using System.Net;
using System.Security.Claims;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger;
using global::Passenger.Data;

public class PassengerGrpcAuthorizationTests : PassengerIntegrationTestBase
{
    public PassengerGrpcAuthorizationTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Theory]
    [InlineData(null)]
    [InlineData("unrelated-api")]
    public async Task should_deny_get_passenger_by_id_without_the_required_scope(string? scope)
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
        var grpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(channel);

        var act = async () => await grpcClient.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });

        await act.Should()
            .ThrowAsync<RpcException>()
            .Where(exception => exception.StatusCode == StatusCode.PermissionDenied);
    }
}
