namespace Unit.Test.Passengers.ValueObjects;

using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class AgeTests
{
    [Fact]
    public void can_create_valid_age()
    {
        var age = Age.Of(25);

        age.Should().NotBeNull();
        age.Value.Should().Be(25);
    }

    [Fact]
    public void zero_age_should_throw_invalid_age_exception()
    {
        var act = () => Age.Of(0);

        act.Should().Throw<InvalidAgeException>();
    }

    [Fact]
    public void negative_age_should_throw_invalid_age_exception()
    {
        var act = () => Age.Of(-5);

        act.Should().Throw<InvalidAgeException>();
    }
}
