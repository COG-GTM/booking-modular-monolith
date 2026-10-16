using Booking.Booking.Exceptions;
using Booking.Booking.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Unit.Test.ValueObjects;

public class PassengerInfoTests
{
    [Fact]
    public void of_keeps_valid_name()
    {
        PassengerInfo.Of("Jane Doe").Name.Should().Be("Jane Doe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void of_rejects_invalid_name(string name)
    {
        FluentActions.Invoking(() => PassengerInfo.Of(name)).Should().Throw<InvalidPassengerNameException>();
    }

    [Fact]
    public void of_returns_equal_records_for_equal_names()
    {
        PassengerInfo.Of("Jane Doe").Should().Be(PassengerInfo.Of("Jane Doe"));
    }
}
