namespace Unit.Test.Passengers.ValueObjects;

using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class PassportNumberTests
{
    [Fact]
    public void can_create_valid_passport_number()
    {
        var passportNumber = PassportNumber.Of("AB123456");

        passportNumber.Should().NotBeNull();
        passportNumber.Value.Should().Be("AB123456");
    }

    [Fact]
    public void null_passport_number_should_throw_invalid_passport_number_exception()
    {
        var act = () => PassportNumber.Of(null!);

        act.Should().Throw<InvalidPassportNumberException>();
    }

    [Fact]
    public void empty_passport_number_should_throw_invalid_passport_number_exception()
    {
        var act = () => PassportNumber.Of(string.Empty);

        act.Should().Throw<InvalidPassportNumberException>();
    }
}
