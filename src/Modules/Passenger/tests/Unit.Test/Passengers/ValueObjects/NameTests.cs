namespace Unit.Test.Passengers.ValueObjects;

using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class NameTests
{
    [Fact]
    public void can_create_valid_name()
    {
        var name = Name.Of("John Doe");

        name.Should().NotBeNull();
        name.Value.Should().Be("John Doe");
    }

    [Fact]
    public void null_name_should_throw_invalid_name_exception()
    {
        var act = () => Name.Of(null!);

        act.Should().Throw<InvalidNameException>();
    }

    [Fact]
    public void empty_name_should_throw_invalid_name_exception()
    {
        var act = () => Name.Of(string.Empty);

        act.Should().Throw<InvalidNameException>();
    }
}
