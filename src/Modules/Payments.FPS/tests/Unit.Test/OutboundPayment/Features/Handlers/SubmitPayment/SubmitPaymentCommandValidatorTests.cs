using FluentAssertions;
using FluentValidation.TestHelper;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.OutboundPayment.Features.Handlers.SubmitPayment;

public class SubmitPaymentCommandValidatorTests
{
    [Fact]
    public void invalid_command_has_errors()
    {
        var result = new SubmitPaymentValidator().TestValidate(new FakeValidateSubmitPaymentCommand().Generate());
        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.Amount);
        result.ShouldHaveValidationErrorFor(x => x.DebtorSortCode);
        result.ShouldHaveValidationErrorFor(x => x.DebtorAccountNumber);
    }
}
