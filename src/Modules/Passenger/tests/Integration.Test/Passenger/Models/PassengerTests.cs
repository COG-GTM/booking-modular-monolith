using FluentAssertions;
using Xunit;

namespace Integration.Test.Passenger.Models;

using global::Passenger.Passengers.ValueObjects;

public class PassengerTests
{
    [Fact]
    public void create_should_set_user_id_on_passenger()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            userId,
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        // Assert
        passenger.UserId.Should().Be(userId);
        passenger.PassportNumber.Value.Should().Be("123456789");
        passenger.Name.Value.Should().Be("Sam");
    }
}
