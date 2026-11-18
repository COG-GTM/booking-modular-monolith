using BuildingBlocks.Constants;
using BuildingBlocks.Exception;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger.Data;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerTests : PassengerIntegrationTestBase
{
    public CompleteRegisterPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_complete_register_passenger_and_update_to_db()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userId
        );

        await Fixture.InsertAsync(passenger);

        Fixture.SetCurrentUser(IdentityConstant.Role.User, userId);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.Name.Should().Be(passenger.Name);
        response?.PassengerDto?.PassportNumber.Should().Be(command.PassportNumber);
        response?.PassengerDto?.PassengerType.ToString().Should().Be(command.PassengerType.ToString());
        response?.PassengerDto?.Age.Should().Be(command.Age);
    }

    [Fact]
    public async Task should_forbid_completing_registration_of_another_users_passenger()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Victim"),
            PassportNumber.Of("987654321"),
            Guid.CreateVersion7()
        );

        await Fixture.InsertAsync(passenger);

        Fixture.SetCurrentUser(IdentityConstant.Role.User, Guid.CreateVersion7());

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var act = () => Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}