using FluentAssertions;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Models;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Unit.Test.OutboundPayment.Features.Domains;

public class OutboundPaymentLifecycleTests
{
    private static Payments.FPS.OutboundPayments.Models.OutboundPayment Create() =>
        Payments.FPS.OutboundPayments.Models.OutboundPayment.Create(
            OutboundPaymentId.Of(Guid.NewGuid()),
            Amount.Of(10m),
            UkAccount.Of("040004", "12345678"),
            UkAccount.Of("202020", "87654321"),
            "REF"
        );

    [Fact]
    public void can_create_valid_payment()
    {
        var x = Create();
        x.Status.Should().Be(PaymentStatus.Initiated);
        x.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void submit_settle_raises_domain_events()
    {
        var x = Create();
        x.Submit();
        x.Settle();
        x.Status.Should().Be(PaymentStatus.Settled);
        x.DomainEvents.Should().HaveCount(3);
    }

    [Fact]
    public void submit_reject_sets_reason_and_raises_event()
    {
        var x = Create();
        x.Submit();
        x.Reject("AC01");
        x.Status.Should().Be(PaymentStatus.Rejected);
        x.RejectionReason.Should().Be("AC01");
        x.DomainEvents.Should().HaveCount(3);
    }

    [Fact]
    public void settle_from_initiated_throws_InvalidPaymentStatusTransitionException()
    {
        var act = () => Create().Settle();
        act.Should().Throw<InvalidPaymentStatusTransitionException>();
    }

    [Fact]
    public void settle_twice_throws()
    {
        var x = Create();
        x.Submit();
        x.Settle();
        var act = () => x.Settle();
        act.Should().Throw<InvalidPaymentStatusTransitionException>();
    }
}
