namespace Unit.Test.Seat.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Seats.Exceptions;
using global::Flight.Seats.ValueObjects;
using Xunit;

public class SeatNumberTests
{
    [Theory]
    [InlineData("12A")]
    [InlineData("1")]
    [InlineData(" 33F ")]
    public void of_with_non_blank_value_should_return_seat_number_with_same_value(string value)
    {
        // Act
        var seatNumber = SeatNumber.Of(value);
        string converted = seatNumber;

        // Assert
        seatNumber.Value.Should().Be(value);
        converted.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void of_with_null_or_whitespace_value_should_throw_invalid_seat_number_exception(string value)
    {
        // Act
        Action act = () => SeatNumber.Of(value);

        // Assert
        act.Should().Throw<InvalidSeatNumberException>();
    }
}
