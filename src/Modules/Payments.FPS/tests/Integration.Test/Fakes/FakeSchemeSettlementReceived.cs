using AutoBogus;
using BuildingBlocks.Contracts.EventBus.Messages;
using MassTransit;

namespace Integration.Test.Fakes;

public sealed class FakeSchemeSettlementReceived : AutoFaker<SchemeSettlementReceived>
{
    public FakeSchemeSettlementReceived()
    {
        RuleFor(x => x.PaymentId, _ => NewId.NextGuid());
        RuleFor(x => x.IsSettled, _ => true);
        RuleFor(x => x.RejectionReason, _ => null);
        RuleFor(x => x.EventId, _ => NewId.NextGuid());
    }
}
