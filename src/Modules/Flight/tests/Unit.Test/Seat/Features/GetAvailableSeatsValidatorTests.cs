using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Unit.Test.Seat.Features;

using global::Flight.Seats.Features.GettingAvailableSeats.V1;

public class GetAvailableSeatsValidatorTests
{
    [Fact]
    public void is_valid_should_be_false_when_flight_id_is_empty()
    {
        var result = new GetAvailableSeatsValidator().TestValidate(new GetAvailableSeats(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.FlightId);
    }

    [Fact]
    public void is_valid_should_be_true_when_flight_id_is_set()
    {
        var result = new GetAvailableSeatsValidator().TestValidate(new GetAvailableSeats(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
