using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Integration.Test.OutboundPayment.Consumers;

public class ReceiveSchemeSettlementInboxTests : PaymentsIntegrationTestBase
{
    public ReceiveSchemeSettlementInboxTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    private static Payments.FPS.OutboundPayments.Models.OutboundPayment SubmittedPayment(Guid id)
    {
        var payment = Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(id),
            Amount.Of(100m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "INBOX"
        );
        payment.Submit();
        return payment;
    }

    private async Task<PersistMessage?> FindInboxRowAsync(Guid eventId)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var persist = scope.ServiceProvider.GetRequiredService<IPersistMessageDbContext>();
        return await persist.PersistMessage.SingleOrDefaultAsync(x =>
            x.Id == eventId && x.DeliveryType == MessageDeliveryType.Inbox
        );
    }

    private async Task WaitForConsumedAsync(Func<IReceivedMessage<SchemeSettlementReceived>, bool> predicate)
    {
        var harness = Fixture.ServiceProvider.GetTestHarness();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (await harness.Consumed.SelectAsync<SchemeSettlementReceived>().AnyAsync(predicate))
                return;
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task should_mark_inbox_message_as_processed_after_settlement()
    {
        var paymentId = NewId.NextGuid();
        await Fixture.InsertAsync(SubmittedPayment(paymentId));
        var message = new SchemeSettlementReceived(paymentId, true, null) { EventId = NewId.NextGuid() };

        await Fixture.Publish(message);
        await WaitForConsumedAsync(x => x.Context.Message.EventId == message.EventId && x.Exception is null);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (
            DateTime.UtcNow < deadline
            && (await FindInboxRowAsync(message.EventId))?.MessageStatus != MessageStatus.Processed
        )
            await Task.Delay(100);

        var inboxRow = await FindInboxRowAsync(message.EventId);
        inboxRow.Should().NotBeNull();
        inboxRow!.MessageStatus.Should().Be(MessageStatus.Processed);
        inboxRow.DataType.Should().Contain(nameof(SchemeSettlementReceived));
    }

    [Fact]
    public async Task should_not_process_inbox_when_payment_is_unknown()
    {
        var unknownPaymentId = NewId.NextGuid();
        var message = new SchemeSettlementReceived(unknownPaymentId, true, null) { EventId = NewId.NextGuid() };

        await Fixture.Publish(message);
        await WaitForConsumedAsync(x => x.Context.Message.EventId == message.EventId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var inboxRow = await FindInboxRowAsync(message.EventId);
        inboxRow.Should().NotBeNull();
        inboxRow!.MessageStatus.Should().NotBe(MessageStatus.Processed);
        var payment = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(unknownPaymentId))
        );
        payment.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_settlement_for_payment_that_is_not_submitted()
    {
        var paymentId = NewId.NextGuid();
        var initiated = Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(paymentId),
            Amount.Of(100m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "INITIATED"
        );
        await Fixture.InsertAsync(initiated);
        var message = new SchemeSettlementReceived(paymentId, true, null) { EventId = NewId.NextGuid() };

        await Fixture.Publish(message);
        await WaitForConsumedAsync(x => x.Context.Message.EventId == message.EventId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var inboxRow = await FindInboxRowAsync(message.EventId);
        inboxRow.Should().NotBeNull();
        inboxRow!.MessageStatus.Should().NotBe(MessageStatus.Processed);
        var payment = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(paymentId))
        );
        payment!.Status.Should().Be(PaymentStatus.Initiated);
    }
}
