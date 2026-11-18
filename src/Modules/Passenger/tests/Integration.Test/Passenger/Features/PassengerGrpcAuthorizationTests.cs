using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class PassengerGrpcAuthorizationTests : PassengerIntegrationTestBase
{
    public PassengerGrpcAuthorizationTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task get_by_id_should_require_authentication()
    {
        using var channel = Fixture.AnonymousChannel;
        var client = new PassengerGrpcService.PassengerGrpcServiceClient(channel);

        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }
}
