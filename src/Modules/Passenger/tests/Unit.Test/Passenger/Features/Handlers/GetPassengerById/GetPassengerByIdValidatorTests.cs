using FluentAssertions;
using FluentValidation.TestHelper;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Passenger.Features.Handlers.GetPassengerById;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

[Collection(nameof(UnitTestFixture))]
public class GetPassengerByIdValidatorTests
{
    [Fact]
    public void is_valid_should_be_true_when_have_valid_parameters()
    {
        // Arrange
        var query = new FakeGetPassengerByIdQuery().Generate();
        var validator = new GetPassengerByIdValidator();

        // Act
        var result = validator.TestValidate(query);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }
}
