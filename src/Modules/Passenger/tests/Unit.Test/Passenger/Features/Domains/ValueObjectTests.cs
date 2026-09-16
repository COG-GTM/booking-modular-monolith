using FluentAssertions;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Passenger.Features.Domains;

using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using InvalidPassengerIdException = global::Passenger.Exceptions.InvalidPassengerIdException;

[Collection(nameof(UnitTestFixture))]
public class ValueObjectTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void age_must_be_positive(int value)
    {
        Action act = () => Age.Of(value);

        act.Should().Throw<InvalidAgeException>();
    }

    [Fact]
    public void age_of_positive_value_should_succeed()
    {
        Age.Of(18).Value.Should().Be(18);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void name_must_not_be_blank(string value)
    {
        Action act = () => Name.Of(value);

        act.Should().Throw<InvalidNameException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void passport_number_must_not_be_blank(string value)
    {
        Action act = () => PassportNumber.Of(value);

        act.Should().Throw<InvalidPassportNumberException>();
    }

    [Fact]
    public void passenger_id_must_not_be_empty()
    {
        Action act = () => PassengerId.Of(Guid.Empty);

        act.Should().Throw<InvalidPassengerIdException>();
    }
}
