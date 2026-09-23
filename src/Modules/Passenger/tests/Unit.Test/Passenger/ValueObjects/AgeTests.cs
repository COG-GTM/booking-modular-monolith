namespace Unit.Test.Passenger.ValueObjects;

using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class AgeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(int.MaxValue)]
    public void of_should_wrap_positive_value(int value)
    {
        // Act
        var age = Age.Of(value);

        // Assert
        age.Value.Should().Be(value);
        ((int)age).Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void of_should_throw_invalid_age_exception_when_value_is_not_positive(int value)
    {
        // Act
        var act = () => Age.Of(value);

        // Assert
        act.Should().Throw<InvalidAgeException>();
    }
}
