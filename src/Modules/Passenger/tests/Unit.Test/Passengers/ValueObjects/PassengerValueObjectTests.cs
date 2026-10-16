using FluentAssertions;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Unit.Test.Passengers.ValueObjects;

public class PassengerValueObjectTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(18)]
    [InlineData(120)]
    public void Age_of_accepts_positive_values_and_converts_to_int(int value)
    {
        Age age = Age.Of(value);

        age.Value.Should().Be(value);
        ((int)age).Should().Be(value);
        age.Should().Be(Age.Of(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99)]
    public void Age_of_rejects_non_positive_values(int value)
    {
        FluentActions
            .Invoking(() => Age.Of(value))
            .Should()
            .Throw<global::Passenger.Passengers.Exceptions.InvalidAgeException>();
    }

    [Fact]
    public void Name_of_accepts_and_converts_a_name()
    {
        Name name = Name.Of("Taylor Passenger");

        name.Value.Should().Be("Taylor Passenger");
        ((string)name).Should().Be("Taylor Passenger");
        name.Should().Be(Name.Of("Taylor Passenger"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Name_of_rejects_empty_names(string? value)
    {
        FluentActions
            .Invoking(() => Name.Of(value!))
            .Should()
            .Throw<global::Passenger.Passengers.Exceptions.InvalidNameException>();
    }

    [Fact]
    public void Passenger_id_of_accepts_and_converts_a_non_empty_guid()
    {
        var value = Guid.NewGuid();
        PassengerId id = PassengerId.Of(value);

        id.Value.Should().Be(value);
        ((Guid)id).Should().Be(value);
        id.Should().Be(PassengerId.Of(value));
    }

    [Fact]
    public void Passenger_id_of_rejects_empty_guid_with_the_value_in_the_message()
    {
        FluentActions
            .Invoking(() => PassengerId.Of(Guid.Empty))
            .Should()
            .Throw<global::Passenger.Exceptions.InvalidPassengerIdException>()
            .WithMessage($"*{Guid.Empty}*");
    }

    [Fact]
    public void Passport_number_of_accepts_converts_and_formats_a_passport_number()
    {
        PassportNumber passportNumber = PassportNumber.Of("AB123456");

        passportNumber.Value.Should().Be("AB123456");
        ((string)passportNumber).Should().Be("AB123456");
        passportNumber.ToString().Should().Be("AB123456");
        passportNumber.Should().Be(PassportNumber.Of("AB123456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Passport_number_of_rejects_empty_values(string? value)
    {
        FluentActions
            .Invoking(() => PassportNumber.Of(value!))
            .Should()
            .Throw<global::Passenger.Passengers.Exceptions.InvalidPassportNumberException>();
    }
}
