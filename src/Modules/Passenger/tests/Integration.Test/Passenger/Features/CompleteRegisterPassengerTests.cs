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
        var userId = Guid.NewGuid();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userId
        );

        await Fixture.InsertAsync(passenger);

        var command = new FakeCompleteRegisterPassengerCommand(
            passenger.PassportNumber,
            passenger.Id,
            userId
        ).Generate();

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
    public async Task should_not_complete_register_passenger_of_another_user()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userA
        );

        await Fixture.InsertAsync(passenger);

        var command = new FakeCompleteRegisterPassengerCommand(
            passenger.PassportNumber,
            passenger.Id,
            userB
        ).Generate();

        var act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<global::Passenger.Exceptions.PassengerNotExist>();
    }
}
