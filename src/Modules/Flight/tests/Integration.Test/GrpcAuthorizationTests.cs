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

namespace Integration.Test;

public class GrpcAuthorizationTests : FlightIntegrationTestBase
{
    public GrpcAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData(null)]
    [InlineData("unrelated-api")]
    public async Task should_reject_authenticated_caller_without_api_scope(string? scope)
    {
        using var client = Fixture.HttpClient;
        var claims = new Dictionary<string, object> { [ClaimTypes.Name] = "test@sample.com" };
        if (scope is not null)
        {
            claims["scope"] = scope;
        }

        client.SetFakeBearerToken(claims);
        using var channel = GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client });
        var grpcClient = new FlightGrpcService.FlightGrpcServiceClient(channel);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => grpcClient.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync
        );

        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }
}
