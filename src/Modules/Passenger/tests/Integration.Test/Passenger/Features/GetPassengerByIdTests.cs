using System.Security.Claims;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using MassTransit;
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
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_currectly()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        SetCurrentUser(command.Id, IdentityConstant.Role.User);

        var query = new GetPassengerById(command.Id);

        // Act
        var response = await Fixture.SendAsync(query);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_not_retrive_another_users_passenger_by_id()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);
        SetCurrentUser(NewId.NextGuid(), IdentityConstant.Role.User);

        var query = new GetPassengerById(command.Id);

        // Act
        var act = () => Fixture.SendAsync(query);

        // Assert
        await Assert.ThrowsAsync<PassengerNotFoundException>(act);
    }

    [Fact]
    public async Task should_retrive_any_passenger_by_id_for_admin()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);
        SetCurrentUser(NewId.NextGuid(), IdentityConstant.Role.Admin);

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
        SetCurrentUser(NewId.NextGuid(), IdentityConstant.Role.Admin);

        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        response?.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id.ToString());
    }

    [Fact]
    public async Task should_not_retrive_another_users_passenger_from_grpc_service()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);
        SetCurrentUser(NewId.NextGuid(), IdentityConstant.Role.User);

        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);

        // Act
        var act = async () => await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        var exception = await Assert.ThrowsAsync<RpcException>(act);
        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    private void SetCurrentUser(Guid subjectId, string role)
    {
        var accessor = Fixture.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext!.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("sub", subjectId.ToString()), new Claim("role", role) }, "Test")
        );
    }
}
