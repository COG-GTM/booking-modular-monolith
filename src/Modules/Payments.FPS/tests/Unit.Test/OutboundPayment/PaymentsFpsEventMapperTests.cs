using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Payments.FPS;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Xunit;

namespace Unit.Test.OutboundPayment;

public class PaymentsFpsEventMapperTests
{
    private readonly PaymentsFpsEventMapper _mapper = new();

    private static PaymentSubmittedDomainEvent SubmittedEvent(Guid id) =>
        new(id, 125.50m, "GBP", "040004", "12345678", "202020", "87654321", "INV-1001", PaymentStatus.Submitted, false);

    [Fact]
    public void maps_submitted_domain_event_to_PaymentSubmitted_integration_event()
    {
        var id = Guid.NewGuid();
        var result = _mapper.MapToIntegrationEvent(SubmittedEvent(id));

        var integrationEvent = result.Should().BeOfType<PaymentSubmitted>().Subject;
        integrationEvent.Id.Should().Be(id);
        integrationEvent.Amount.Should().Be(125.50m);
        integrationEvent.Currency.Should().Be("GBP");
        integrationEvent.DebtorSortCode.Should().Be("040004");
        integrationEvent.DebtorAccountNumber.Should().Be("12345678");
        integrationEvent.CreditorSortCode.Should().Be("202020");
        integrationEvent.CreditorAccountNumber.Should().Be("87654321");
        integrationEvent.Reference.Should().Be("INV-1001");
    }

    [Fact]
    public void maps_settled_domain_event_to_PaymentSettled_integration_event()
    {
        var id = Guid.NewGuid();
        var result = _mapper.MapToIntegrationEvent(new PaymentSettledDomainEvent(id, PaymentStatus.Settled));

        result.Should().BeOfType<PaymentSettled>().Which.Id.Should().Be(id);
    }

    [Fact]
    public void maps_rejected_domain_event_to_PaymentRejected_integration_event()
    {
        var id = Guid.NewGuid();
        var result = _mapper.MapToIntegrationEvent(new PaymentRejectedDomainEvent(id, PaymentStatus.Rejected, "AC01"));

        var rejected = result.Should().BeOfType<PaymentRejected>().Subject;
        rejected.Id.Should().Be(id);
        rejected.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public void initiated_domain_event_has_no_integration_event()
    {
        var initiated = new PaymentInitiatedDomainEvent(
            Guid.NewGuid(),
            10m,
            "GBP",
            "040004",
            "12345678",
            "202020",
            "87654321",
            "REF",
            PaymentStatus.Initiated,
            false
        );

        _mapper.MapToIntegrationEvent(initiated).Should().BeNull();
        _mapper.MapToInternalCommand(initiated).Should().BeNull();
    }

    [Fact]
    public void maps_submitted_domain_event_to_CreateOutboundPaymentMongo_internal_command()
    {
        var id = Guid.NewGuid();
        var result = _mapper.MapToInternalCommand(SubmittedEvent(id));

        var command = result.Should().BeOfType<CreateOutboundPaymentMongo>().Subject;
        command.Id.Should().Be(id);
        command.Amount.Should().Be(125.50m);
        command.Currency.Should().Be("GBP");
        command.DebtorSortCode.Should().Be("040004");
        command.DebtorAccountNumber.Should().Be("12345678");
        command.CreditorSortCode.Should().Be("202020");
        command.CreditorAccountNumber.Should().Be("87654321");
        command.Reference.Should().Be("INV-1001");
        command.Status.Should().Be(PaymentStatus.Submitted);
        command.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void maps_settled_domain_event_to_UpdateOutboundPaymentMongo_without_rejection_reason()
    {
        var id = Guid.NewGuid();
        var result = _mapper.MapToInternalCommand(new PaymentSettledDomainEvent(id, PaymentStatus.Settled));

        var command = result.Should().BeOfType<UpdateOutboundPaymentMongo>().Subject;
        command.Id.Should().Be(id);
        command.Status.Should().Be(PaymentStatus.Settled);
        command.RejectionReason.Should().BeNull();
        command.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void maps_rejected_domain_event_to_UpdateOutboundPaymentMongo_with_rejection_reason()
    {
        var id = Guid.NewGuid();
        var result = _mapper.MapToInternalCommand(new PaymentRejectedDomainEvent(id, PaymentStatus.Rejected, "AC01"));

        var command = result.Should().BeOfType<UpdateOutboundPaymentMongo>().Subject;
        command.Id.Should().Be(id);
        command.Status.Should().Be(PaymentStatus.Rejected);
        command.RejectionReason.Should().Be("AC01");
    }

    [Fact]
    public void unknown_domain_event_maps_to_null()
    {
        var unknown = new UnknownDomainEvent();

        _mapper.MapToIntegrationEvent(unknown).Should().BeNull();
        _mapper.MapToInternalCommand(unknown).Should().BeNull();
    }

    private sealed record UnknownDomainEvent : IDomainEvent;
}
