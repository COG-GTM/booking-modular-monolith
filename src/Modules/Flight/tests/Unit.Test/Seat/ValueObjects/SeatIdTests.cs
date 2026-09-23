namespace Unit.Test.Seat.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Seats.Exceptions;
using global::Flight.Seats.ValueObjects;
using Xunit;

public class SeatIdTests
{
    [Fact]
    public void of_with_non_empty_guid_should_return_seat_id_with_same_value()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var seatId = SeatId.Of(guid);
        Guid converted = seatId;

        // Assert
        seatId.Value.Should().Be(guid);
        converted.Should().Be(guid);
    }

    [Fact]
    public void of_with_empty_guid_should_throw_invalid_seat_id_exception()
    {
        // Act
        Action act = () => SeatId.Of(Guid.Empty);

        // Assert
        act.Should().Throw<InvalidSeatIdException>();
    }
}
