using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Passenger;
using Passenger.Data;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class GetPassengerByIdTests : PassengerIntegrationTestBase
{
    public GetPassengerByIdTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_currectly()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetPassengerById(command.Id);

        // Act
        var response = await Fixture.SendAsync(query);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_from_grpc_service()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        response?.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id.ToString());
    }

    [Fact]
    public async Task should_return_passenger_to_its_owner()
    {
        var ownerId = NewId.NextGuid();
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = ownerId };
        await Fixture.SendAsync(command);

        var response = await Fixture.SendAsync(
            new GetPassengerById(command.Id, new PassengerRequester(ownerId, IsAdmin: false)));

        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_not_return_passenger_to_another_user()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = NewId.NextGuid() };
        await Fixture.SendAsync(command);

        var act = () => Fixture.SendAsync(
            new GetPassengerById(command.Id, new PassengerRequester(NewId.NextGuid(), IsAdmin: false)));

        await act.Should().ThrowAsync<PassengerNotFoundException>();
    }

    [Fact]
    public async Task should_not_return_ownerless_passenger_to_non_admin_user()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = null };
        await Fixture.SendAsync(command);

        var act = () => Fixture.SendAsync(
            new GetPassengerById(command.Id, new PassengerRequester(NewId.NextGuid(), IsAdmin: false)));

        await act.Should().ThrowAsync<PassengerNotFoundException>();
    }

    [Fact]
    public async Task should_return_any_passenger_to_admin()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = NewId.NextGuid() };
        await Fixture.SendAsync(command);

        var response = await Fixture.SendAsync(
            new GetPassengerById(command.Id, new PassengerRequester(NewId.NextGuid(), IsAdmin: true)));

        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_return_not_found_over_http_when_caller_does_not_own_passenger()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = NewId.NextGuid() };
        await Fixture.SendAsync(command);

        var client = CreateUserClient(NewId.NextGuid());

        var response = await client.GetAsync($"api/v1/passenger/{command.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(command.PassportNumber);
    }

    [Fact]
    public async Task should_return_passenger_over_http_to_its_owner()
    {
        var ownerId = NewId.NextGuid();
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = ownerId };
        await Fixture.SendAsync(command);

        var client = CreateUserClient(ownerId);

        var response = await client.GetAsync($"api/v1/passenger/{command.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(command.Id.ToString());
    }

    [Fact]
    public async Task should_link_new_passenger_to_registering_user()
    {
        var userCreated = new FakeUserCreated().Generate() with
        {
            PassportNumber = Random.Shared.Next(100000000, 999999999).ToString()
        };

        await Fixture.Publish(userCreated);

        global::Passenger.Passengers.Models.Passenger? passenger = null;
        for (var attempt = 0; attempt < 100 && passenger is null; attempt++)
        {
            passenger = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.SingleOrDefaultAsync(x => x.PassportNumber.Value == userCreated.PassportNumber));

            if (passenger is null)
            {
                await Task.Delay(100);
            }
        }

        passenger.Should().NotBeNull();
        passenger!.UserId.Should().Be(userCreated.Id);
    }

    private HttpClient CreateUserClient(Guid userId)
    {
        var client = Fixture.HttpClient;
        client.SetFakeBearerToken(new Dictionary<string, object>
        {
            { "sub", userId.ToString() },
            { ClaimTypes.Role, "user" },
            { "scope", "flight-api" },
        });
        return client;
    }
}
