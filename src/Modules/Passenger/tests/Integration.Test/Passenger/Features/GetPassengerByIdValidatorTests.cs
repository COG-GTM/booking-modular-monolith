using FluentAssertions;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class GetPassengerByIdValidatorTests
{
    [Fact]
    public void should_reject_empty_passenger_id()
    {
        var result = new GetPassengerByIdValidator().Validate(new GetPassengerById(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(GetPassengerById.Id));
    }

    [Fact]
    public void should_accept_non_empty_passenger_id()
    {
        var result = new GetPassengerByIdValidator().Validate(new GetPassengerById(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
