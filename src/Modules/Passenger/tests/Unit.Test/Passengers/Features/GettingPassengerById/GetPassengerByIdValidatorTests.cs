using FluentAssertions;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Unit.Test.Passengers.Features.GettingPassengerById;

public class GetPassengerByIdValidatorTests
{
    [Fact]
    public void Non_empty_id_passes_validation()
    {
        var result = new GetPassengerByIdValidator().Validate(new GetPassengerById(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
