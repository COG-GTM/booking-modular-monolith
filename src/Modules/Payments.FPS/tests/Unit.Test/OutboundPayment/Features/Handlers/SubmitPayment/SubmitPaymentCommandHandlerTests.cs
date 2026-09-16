namespace Unit.Test.OutboundPayment.Features.Handlers.SubmitPayment;

using FluentAssertions;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Payments.FPS.OutboundPayments.ValueObjects;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class SubmitPaymentCommandHandlerTests
{
    private readonly SubmitPaymentHandler _handler;
    private readonly UnitTestFixture _fixture;

    public SubmitPaymentCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new SubmitPaymentHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_creates_submitted_payment()
    {
        var command = new FakeSubmitPaymentCommand().Generate();
        var response = await _handler.Handle(command, CancellationToken.None);
        var payment = await _fixture.DbContext.OutboundPayments.FindAsync(OutboundPaymentId.Of(response.Id));
        payment.Should().NotBeNull();
        payment!.Status.Should().Be(PaymentStatus.Submitted);
    }

    [Fact]
    public async Task null_command_throws()
    {
        SubmitPayment command = null!;
        Func<Task> act = () => _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
