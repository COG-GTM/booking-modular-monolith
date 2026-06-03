namespace Unit.Test.Passengers.Features;

using FluentAssertions;
using FluentValidation.TestHelper;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class CompleteRegisterPassengerValidatorTests
{
    [Fact]
    public void is_valid_should_be_false_when_have_invalid_parameters()
    {
        var command = FakeValidateCompleteRegisterPassengerCommand.Generate();
        var validator = new CompleteRegisterPassengerValidator();

        var result = validator.TestValidate(command);

        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.PassportNumber);
        result.ShouldHaveValidationErrorFor(x => x.Age);
        result.ShouldHaveValidationErrorFor(x => x.PassengerType);
    }
}
