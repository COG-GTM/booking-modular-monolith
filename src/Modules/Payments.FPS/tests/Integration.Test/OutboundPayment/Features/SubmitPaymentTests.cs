using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Integration.Test.Fakes;
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Integration.Test.OutboundPayment.Features;

public class SubmitPaymentTests : PaymentsIntegrationTestBase
{
    public SubmitPaymentTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_submit_payment_to_db_and_publish_message_to_broker()
    {
        var command = new FakeSubmitPaymentCommand().Generate();
        var response = await Fixture.SendAsync(command);
        response.Id.Should().Be(command.Id);
        (await Fixture.WaitForPublishing<PaymentSubmitted>()).Should().BeTrue();
        var payment = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(response.Id))
        );
        payment!.Status.Should().Be(PaymentStatus.Submitted);
    }
}
