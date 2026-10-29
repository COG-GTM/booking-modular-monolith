using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Infrastructure.Tests;

public record SeatReserved : IDomainEvent;

public record SeatReservedIntegration : IIntegrationEvent;

public record SeatReleasedIntegration : IIntegrationEvent;

public record ConfirmSeat : IInternalCommand;

public sealed class SeatMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
        @event is SeatReserved ? new SeatReservedIntegration() : null;

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event) =>
        @event is SeatReserved ? new ConfirmSeat() : null;
}
