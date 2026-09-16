namespace Unit.Test.Booking.Features.Domains;

using System;
using FluentAssertions;
using global::Booking.Booking.Exceptions;
using global::Booking.Booking.ValueObjects;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class PassengerInfoTests
{
    [Fact]
    public void can_create_valid_passenger_info()
    {
        var passengerInfo = PassengerInfo.Of("Sam");

        passengerInfo.Name.Should().Be("Sam");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void empty_name_should_throw(string name)
    {
        Action act = () => PassengerInfo.Of(name);

        act.Should().Throw<InvalidPassengerNameException>();
    }
}
