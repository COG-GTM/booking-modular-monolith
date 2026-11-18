using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test;

public class GrpcScopeAuthorizationTests : PassengerIntegrationTestBase
{
    public GrpcScopeAuthorizationTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task should_reject_authenticated_grpc_call_without_required_scope()
    {
        using var client = Fixture.AnonymousHttpClient;
        client.SetFakeBearerToken(
            new Dictionary<string, object> { ["sub"] = "wrong-scope-user", ["scope"] = "unrelated-api" }
        );
        using var channel = GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client });
        var grpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(channel);

        var act = async () => await grpcClient.GetByIdAsync(new GetByIdRequest { Id = "1" }).ResponseAsync;

        await act.Should()
            .ThrowAsync<RpcException>()
            .Where(exception => exception.StatusCode == StatusCode.PermissionDenied);
    }
}
