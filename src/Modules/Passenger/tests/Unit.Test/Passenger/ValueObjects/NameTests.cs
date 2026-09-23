namespace Unit.Test.Passenger.ValueObjects;

using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class NameTests
{
    [Theory]
    [InlineData("Sam")]
    [InlineData("Mary Jane")]
    public void of_should_wrap_non_empty_value(string value)
    {
        // Act
        var name = Name.Of(value);

        // Assert
        name.Value.Should().Be(value);
        ((string)name).Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void of_should_throw_invalid_name_exception_when_value_is_null_or_whitespace(string? value)
    {
        // Act
        var act = () => Name.Of(value!);

        // Assert
        act.Should().Throw<InvalidNameException>();
    }
}
