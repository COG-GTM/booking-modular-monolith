using System.Net;
using System.Security.Claims;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger;
using Passenger.Data;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Passenger.Passengers.ValueObjects;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class GetPassengerByIdTests : PassengerIntegrationTestBase
{
    public GetPassengerByIdTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task should_retrieve_passenger_internally_without_user_context()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);
        var result = await Fixture.SendAsync(new GetPassengerById(command.Id));
        result.PassengerDto.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_retrieve_passenger_from_grpc_without_forwarded_token()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);
        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;
        using var channel = Grpc.Net.Client.GrpcChannel.ForAddress(
            client.BaseAddress!,
            new Grpc.Net.Client.GrpcChannelOptions { HttpClient = client }
        );
        var grpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(channel);
        var response = await grpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });
        response.PassengerDto.Id.Should().Be(command.Id.ToString());
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.OK)]
    [InlineData(true, true, HttpStatusCode.OK)]
    [InlineData(true, false, HttpStatusCode.NotFound)]
    public async Task should_enforce_ownership_only_for_owned_http_passengers(
        bool owned,
        bool owner,
        HttpStatusCode expected
    )
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        var userId = Guid.NewGuid();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(command.Id),
            Name.Of(command.Name),
            PassportNumber.Of(command.PassportNumber),
            userId: owned ? userId : null
        );
        await Fixture.InsertAsync(passenger);
        await Fixture.SendAsync(command);
        using var client = Fixture.HttpClient;
        client.SetFakeBearerToken(
            new Dictionary<string, object>
            {
                ["sub"] = (owner ? userId : Guid.NewGuid()).ToString(),
                [ClaimTypes.Role] = "user",
                ["scope"] = "flight-api",
            }
        );

        var response = await client.GetAsync($"/api/v1/passenger/{command.Id}");
        response.StatusCode.Should().Be(expected);
    }
}
