using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Integration.Test.Fakes;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test.GrpcServer;

public class PassengerGrpcAuthorizationTests : PassengerIntegrationTestBase
{
    public PassengerGrpcAuthorizationTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_deny_get_passenger_by_id_for_token_without_api_scope()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);

        var httpClient = Fixture.HttpClient;
        httpClient.SetFakeBearerToken(
            new Dictionary<string, object>
            {
                { ClaimTypes.Name, "test@sample.com" },
                { ClaimTypes.Role, "admin" },
                { "scope", "passenger-api" },
            }
        );

        var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient }
        );
        var client = new PassengerGrpcService.PassengerGrpcServiceClient(channel);

        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }
}
