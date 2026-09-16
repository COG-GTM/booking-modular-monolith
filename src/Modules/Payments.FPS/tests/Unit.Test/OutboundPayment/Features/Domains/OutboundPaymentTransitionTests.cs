using FluentAssertions;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Unit.Test.OutboundPayment.Features.Domains;

public class OutboundPaymentTransitionTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private static Payments.FPS.OutboundPayments.Models.OutboundPayment Create(bool isDeleted = false) =>
        Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(Id),
            Amount.Of(125.50m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "INV-1001",
            isDeleted
        );

    [Fact]
    public void create_populates_all_fields_and_raises_initiated_event()
    {
        var payment = Create();

        payment.Id.Value.Should().Be(Id);
        payment.Amount.Value.Should().Be(125.50m);
        payment.Amount.Currency.Should().Be("GBP");
        payment.DebtorAccount.SortCode.Should().Be("040004");
        payment.DebtorAccount.AccountNumber.Should().Be("12345678");
        payment.CreditorAccount.SortCode.Should().Be("202020");
        payment.CreditorAccount.AccountNumber.Should().Be("87654321");
        payment.Reference.Should().Be("INV-1001");
        payment.RejectionReason.Should().BeNull();
        payment.IsDeleted.Should().BeFalse();

        var initiated = payment.DomainEvents.Single().Should().BeOfType<PaymentInitiatedDomainEvent>().Subject;
        initiated.Id.Should().Be(Id);
        initiated.Amount.Should().Be(125.50m);
        initiated.Currency.Should().Be("GBP");
        initiated.DebtorSortCode.Should().Be("040004");
        initiated.DebtorAccountNumber.Should().Be("12345678");
        initiated.CreditorSortCode.Should().Be("202020");
        initiated.CreditorAccountNumber.Should().Be("87654321");
        initiated.Reference.Should().Be("INV-1001");
        initiated.Status.Should().Be(PaymentStatus.Initiated);
        initiated.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void create_with_is_deleted_propagates_flag_to_event()
    {
        var payment = Create(isDeleted: true);

        payment.IsDeleted.Should().BeTrue();
        payment
            .DomainEvents.Single()
            .Should()
            .BeOfType<PaymentInitiatedDomainEvent>()
            .Which.IsDeleted.Should()
            .BeTrue();
    }

    [Fact]
    public void submit_raises_submitted_event_with_submitted_status()
    {
        var payment = Create();
        payment.Submit();

        payment.Status.Should().Be(PaymentStatus.Submitted);
        var submitted = payment.DomainEvents.Last().Should().BeOfType<PaymentSubmittedDomainEvent>().Subject;
        submitted.Id.Should().Be(Id);
        submitted.Amount.Should().Be(125.50m);
        submitted.Currency.Should().Be("GBP");
        submitted.DebtorSortCode.Should().Be("040004");
        submitted.DebtorAccountNumber.Should().Be("12345678");
        submitted.CreditorSortCode.Should().Be("202020");
        submitted.CreditorAccountNumber.Should().Be("87654321");
        submitted.Reference.Should().Be("INV-1001");
        submitted.Status.Should().Be(PaymentStatus.Submitted);
    }

    [Fact]
    public void settle_raises_settled_event_with_payment_id()
    {
        var payment = Create();
        payment.Submit();
        payment.Settle();

        var settled = payment.DomainEvents.Last().Should().BeOfType<PaymentSettledDomainEvent>().Subject;
        settled.Id.Should().Be(Id);
        settled.Status.Should().Be(PaymentStatus.Settled);
        payment.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void reject_raises_rejected_event_with_reason()
    {
        var payment = Create();
        payment.Submit();
        payment.Reject("AC01");

        var rejected = payment.DomainEvents.Last().Should().BeOfType<PaymentRejectedDomainEvent>().Subject;
        rejected.Id.Should().Be(Id);
        rejected.Status.Should().Be(PaymentStatus.Rejected);
        rejected.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public void submit_twice_throws()
    {
        var payment = Create();
        payment.Submit();

        var act = () => payment.Submit();

        act.Should()
            .Throw<InvalidPaymentStatusTransitionException>()
            .WithMessage("Invalid payment status transition from Submitted to Submitted.");
        payment.Status.Should().Be(PaymentStatus.Submitted);
        payment.DomainEvents.Should().HaveCount(2);
    }

    [Fact]
    public void reject_from_initiated_throws()
    {
        var payment = Create();

        var act = () => payment.Reject("AC01");

        act.Should()
            .Throw<InvalidPaymentStatusTransitionException>()
            .WithMessage("Invalid payment status transition from Initiated to Rejected.");
        payment.Status.Should().Be(PaymentStatus.Initiated);
        payment.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void settle_after_reject_throws()
    {
        var payment = Create();
        payment.Submit();
        payment.Reject("AC01");

        var act = () => payment.Settle();

        act.Should()
            .Throw<InvalidPaymentStatusTransitionException>()
            .WithMessage("Invalid payment status transition from Rejected to Settled.");
        payment.Status.Should().Be(PaymentStatus.Rejected);
    }

    [Fact]
    public void reject_after_settle_throws()
    {
        var payment = Create();
        payment.Submit();
        payment.Settle();

        var act = () => payment.Reject("AC01");

        act.Should()
            .Throw<InvalidPaymentStatusTransitionException>()
            .WithMessage("Invalid payment status transition from Settled to Rejected.");
        payment.Status.Should().Be(PaymentStatus.Settled);
        payment.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void submit_after_settle_throws()
    {
        var payment = Create();
        payment.Submit();
        payment.Settle();

        var act = () => payment.Submit();

        act.Should().Throw<InvalidPaymentStatusTransitionException>();
        payment.DomainEvents.Should().HaveCount(3);
    }
}
