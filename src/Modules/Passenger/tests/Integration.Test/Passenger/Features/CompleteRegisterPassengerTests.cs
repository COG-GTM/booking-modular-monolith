using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger.Data;
using Passenger.Passengers.Exceptions;
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
        Fixture.SetCurrentUser(userId, IdentityConstant.Role.User);

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
    public async Task should_throw_access_denied_when_caller_is_not_passenger_owner()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("987654321"),
            Guid.CreateVersion7()
        );

        await Fixture.InsertAsync(passenger);
        Fixture.SetCurrentUser(Guid.CreateVersion7(), IdentityConstant.Role.User);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<PassengerAccessDeniedException>();
    }

    [Fact]
    public async Task should_throw_access_denied_when_caller_identity_is_missing()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("555555555"),
            Guid.CreateVersion7()
        );

        await Fixture.InsertAsync(passenger);
        Fixture.ClearCurrentUser();

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<PassengerAccessDeniedException>();
    }

    [Fact]
    public async Task should_allow_admin_to_complete_register_any_passenger()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("111111111"),
            Guid.CreateVersion7()
        );

        await Fixture.InsertAsync(passenger);
        Fixture.SetCurrentUser(Guid.CreateVersion7(), IdentityConstant.Role.Admin);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.PassportNumber.Should().Be(command.PassportNumber);
        response?.PassengerDto?.Age.Should().Be(command.Age);
    }
}
