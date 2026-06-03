using Booking.Booking.Exceptions;
using Booking.Booking.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Booking.ValueObjects;

[Collection(nameof(Common.UnitTestFixture))]
public class PassengerInfoTests
{
    [Fact]
    public void can_create_passenger_info_with_valid_name()
    {
        var name = "John Doe";

        var passengerInfo = PassengerInfo.Of(name);

        passengerInfo.Should().NotBeNull();
        passengerInfo.Name.Should().Be(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void null_or_empty_name_throws_invalid_passenger_name_exception(string? name)
    {
        var act = () => PassengerInfo.Of(name!);

        act.Should().Throw<InvalidPassengerNameException>();
    }
}
