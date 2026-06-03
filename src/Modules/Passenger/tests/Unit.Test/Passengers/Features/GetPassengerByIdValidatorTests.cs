namespace Unit.Test.Passengers.Features;

using FluentAssertions;
using FluentValidation.TestHelper;
using global::Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

public class GetPassengerByIdValidatorTests
{
    [Fact]
    public void is_valid_should_be_true_when_id_is_provided()
    {
        var query = new GetPassengerById(Guid.NewGuid());
        var validator = new GetPassengerByIdValidator();

        var result = validator.TestValidate(query);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void is_valid_should_not_have_error_when_id_is_empty_guid()
    {
        var query = new GetPassengerById(Guid.Empty);
        var validator = new GetPassengerByIdValidator();

        var result = validator.TestValidate(query);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }
}
