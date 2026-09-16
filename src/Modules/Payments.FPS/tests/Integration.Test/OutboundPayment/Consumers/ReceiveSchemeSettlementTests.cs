using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Integration.Test.OutboundPayment.Consumers;

public class ReceiveSchemeSettlementTests : PaymentsIntegrationTestBase
{
    public ReceiveSchemeSettlementTests(
        BuildingBlocks.TestBase.TestFixture<
            Api.Program,
            Payments.FPS.Data.PaymentsDbContext,
            Payments.FPS.Data.PaymentsReadDbContext
        > fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task should_process_duplicate_scheme_settlement_once()
    {
        var paymentId = NewId.NextGuid();
        var payment = Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(paymentId),
            Amount.Of(100m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "SETTLEMENT"
        );
        payment.Submit();
        await Fixture.InsertAsync(payment);
        await Fixture.InsertMongoDbContextAsync(
            "outbound_payment",
            new OutboundPaymentReadModel
            {
                Id = NewId.NextGuid(),
                OutboundPaymentId = paymentId,
                Amount = 100m,
                Currency = "GBP",
                DebtorSortCode = "040004",
                DebtorAccountNumber = "12345678",
                CreditorSortCode = "202020",
                CreditorAccountNumber = "87654321",
                Reference = "SETTLEMENT",
                Status = PaymentStatus.Submitted,
                IsDeleted = false,
            }
        );
        var initialVersion = payment.Version;
        var message = new SchemeSettlementReceived(paymentId, true, null) { EventId = NewId.NextGuid() };

        await Fixture.Publish(message);
        await Fixture.Publish(message);
        var consumed = Fixture.ServiceProvider.GetTestHarness().Consumed.SelectAsync<SchemeSettlementReceived>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (await consumed.CountAsync() < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        (await Fixture.WaitForPublishing<PaymentSettled>()).Should().BeTrue();

        var storedPayment = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(paymentId))
        );
        storedPayment!.Status.Should().Be(PaymentStatus.Settled);
        storedPayment.Version.Should().Be(initialVersion + 1);
        using var scope = Fixture.ServiceProvider.CreateScope();
        var persist = scope.ServiceProvider.GetRequiredService<IPersistMessageDbContext>();
        var rows = await persist.PersistMessage.ToListAsync();
        rows.Count(x => x.Id == message.EventId && x.DeliveryType == MessageDeliveryType.Inbox).Should().Be(1);
        rows.Count(x =>
                x.DataType.Contains(nameof(UpdateOutboundPaymentMongo), StringComparison.Ordinal)
                && x.DeliveryType == MessageDeliveryType.Internal
            )
            .Should()
            .Be(1);
        rows.Count(x =>
                x.DataType.Contains(nameof(PaymentSettled), StringComparison.Ordinal)
                && x.DeliveryType == MessageDeliveryType.Outbox
            )
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_reject_payment_when_scheme_reports_rejection()
    {
        var paymentId = NewId.NextGuid();
        var payment = Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(paymentId),
            Amount.Of(100m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "REJECTION"
        );
        payment.Submit();
        await Fixture.InsertAsync(payment);
        var message = new SchemeSettlementReceived(paymentId, false, "AC01") { EventId = NewId.NextGuid() };
        await Fixture.Publish(message);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var current = await Fixture.ExecuteDbContextAsync(db =>
                db.OutboundPayments.FindAsync(OutboundPaymentId.Of(paymentId))
            );
            if (current?.Status == PaymentStatus.Rejected)
                break;
            await Task.Delay(100);
        }
        var rejected = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(paymentId))
        );
        rejected!.Status.Should().Be(PaymentStatus.Rejected);
        rejected.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public async Task should_apply_settlement_on_redelivery_after_failed_attempt()
    {
        var paymentId = NewId.NextGuid();
        var message = new SchemeSettlementReceived(paymentId, true, null) { EventId = NewId.NextGuid() };

        await Fixture.Publish(message);
        var harness = Fixture.ServiceProvider.GetTestHarness();
        var consumed = harness.Consumed.SelectAsync<SchemeSettlementReceived>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (
            DateTime.UtcNow < deadline && !await consumed.AnyAsync(x => x.Context.Message.EventId == message.EventId)
        )
            await Task.Delay(100);

        var inboxDeadline = DateTime.UtcNow.AddSeconds(30);
        PersistMessage? inboxRow = null;
        while (DateTime.UtcNow < inboxDeadline && inboxRow is null)
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var persist = scope.ServiceProvider.GetRequiredService<IPersistMessageDbContext>();
            inboxRow = await persist.PersistMessage.SingleOrDefaultAsync(x =>
                x.Id == message.EventId && x.DeliveryType == MessageDeliveryType.Inbox
            );
            if (inboxRow is null)
                await Task.Delay(100);
        }
        inboxRow.Should().NotBeNull();
        inboxRow!.MessageStatus.Should().NotBe(MessageStatus.Processed);

        var payment = Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(paymentId),
            Amount.Of(100m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "REDELIVERY"
        );
        payment.Submit();
        await Fixture.InsertAsync(payment);
        await Fixture.InsertMongoDbContextAsync(
            "outbound_payment",
            new OutboundPaymentReadModel
            {
                Id = NewId.NextGuid(),
                OutboundPaymentId = paymentId,
                Amount = 100m,
                Currency = "GBP",
                DebtorSortCode = "040004",
                DebtorAccountNumber = "12345678",
                CreditorSortCode = "202020",
                CreditorAccountNumber = "87654321",
                Reference = "REDELIVERY",
                Status = PaymentStatus.Submitted,
                IsDeleted = false,
            }
        );

        await Fixture.Publish(message);
        var settledDeadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < settledDeadline)
        {
            var current = await Fixture.ExecuteDbContextAsync(db =>
                db.OutboundPayments.FindAsync(OutboundPaymentId.Of(paymentId))
            );
            if (current?.Status == PaymentStatus.Settled)
                break;
            await Task.Delay(100);
        }

        var settled = await Fixture.ExecuteDbContextAsync(db =>
            db.OutboundPayments.FindAsync(OutboundPaymentId.Of(paymentId))
        );
        settled!.Status.Should().Be(PaymentStatus.Settled);
        using var finalScope = Fixture.ServiceProvider.CreateScope();
        var finalPersist = finalScope.ServiceProvider.GetRequiredService<IPersistMessageDbContext>();
        var inboxRows = await finalPersist
            .PersistMessage.Where(x => x.Id == message.EventId && x.DeliveryType == MessageDeliveryType.Inbox)
            .ToListAsync();
        inboxRows.Should().ContainSingle();
        inboxRows[0].MessageStatus.Should().Be(MessageStatus.Processed);
    }
}
