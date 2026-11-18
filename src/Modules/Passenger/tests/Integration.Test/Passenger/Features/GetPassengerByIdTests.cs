using System.Security.Claims;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Passenger.Data;
using Passenger.Passengers.Exceptions;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class GetPassengerByIdTests : PassengerIntegrationTestBase
{
    public GetPassengerByIdTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_currectly()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        SetCurrentUser(command.Id);
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

        SetCurrentUser(command.Id);
        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        response?.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id.ToString());
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task should_return_passenger_for_authenticated_owner(string claimType)
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);
        SetCurrentUser(command.Id, claimType);

        var result = await Fixture.SendAsync(new GetPassengerById(command.Id));

        result.PassengerDto.Id.Should().Be(command.Id);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("admin")]
    public async Task should_not_return_another_users_passenger(string role)
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);
        SetCurrentUser(Guid.NewGuid(), role: role);

        await Assert.ThrowsAsync<PassengerNotFoundException>(() => Fixture.SendAsync(new GetPassengerById(command.Id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_not_return_missing_or_deleted_passenger(bool deleted)
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { IsDeleted = true };
        if (deleted)
        {
            await Fixture.SendAsync(command);
        }
        SetCurrentUser(command.Id);

        await Assert.ThrowsAsync<PassengerNotFoundException>(() => Fixture.SendAsync(new GetPassengerById(command.Id)));
    }

    [Fact]
    public async Task should_not_use_name_identifier_when_subject_belongs_to_another_user()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);
        SetCurrentUser(command.Id, ClaimTypes.NameIdentifier);
        var user = Fixture.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("sub", Guid.NewGuid().ToString()));

        await Assert.ThrowsAsync<PassengerNotFoundException>(() => Fixture.SendAsync(new GetPassengerById(command.Id)));
    }

    private void SetCurrentUser(Guid id, string claimType = "sub", string role = "user")
    {
        var accessor = Fixture.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(claimType, id.ToString()), new Claim(ClaimTypes.Role, role) }, "Test")
        );
    }
}
