namespace Unit.Test.Passengers.ValueObjects;

using FluentAssertions;
using global::Passenger.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class PassengerIdTests
{
    [Fact]
    public void can_create_valid_passenger_id()
    {
        var guid = Guid.NewGuid();
        var passengerId = PassengerId.Of(guid);

        passengerId.Should().NotBeNull();
        passengerId.Value.Should().Be(guid);
    }

    [Fact]
    public void empty_guid_should_throw_invalid_passenger_id_exception()
    {
        var act = () => PassengerId.Of(Guid.Empty);

        act.Should().Throw<InvalidPassengerIdException>();
    }
}
