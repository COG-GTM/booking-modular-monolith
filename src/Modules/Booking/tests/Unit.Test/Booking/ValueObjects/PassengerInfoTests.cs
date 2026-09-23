namespace Unit.Test.Booking.ValueObjects;

using FluentAssertions;
using global::Booking.Booking.Exceptions;
using global::Booking.Booking.ValueObjects;
using Xunit;

public class PassengerInfoTests
{
    [Fact]
    public void can_create_valid_passenger_info()
    {
        // Arrange + Act
        var passengerInfo = PassengerInfo.Of("Jane Doe");

        // Assert
        passengerInfo.Should().NotBeNull();
        passengerInfo.Name.Should().Be("Jane Doe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_name_is_blank(string? name)
    {
        // Act
        var act = () => PassengerInfo.Of(name!);

        // Assert
        act.Should().ThrowExactly<InvalidPassengerNameException>();
    }
}
