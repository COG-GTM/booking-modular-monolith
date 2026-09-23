namespace Unit.Test.Passenger.ValueObjects;

using System;
using FluentAssertions;
using global::Passenger.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using MassTransit;
using Xunit;

public class PassengerIdTests
{
    [Fact]
    public void of_should_wrap_non_empty_guid()
    {
        // Arrange
        var value = NewId.NextGuid();

        // Act
        var passengerId = PassengerId.Of(value);

        // Assert
        passengerId.Value.Should().Be(value);
        ((Guid)passengerId).Should().Be(value);
    }

    [Fact]
    public void of_should_throw_invalid_passenger_id_exception_when_guid_is_empty()
    {
        // Act
        var act = () => PassengerId.Of(Guid.Empty);

        // Assert
        act.Should().Throw<InvalidPassengerIdException>().WithMessage($"*{Guid.Empty}*");
    }
}
