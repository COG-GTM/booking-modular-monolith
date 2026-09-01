using BuildingBlocks.Core.Event;

namespace SharedContracts.EventBus.Messages;

public record UserCreated(Guid Id, string Name, string PassportNumber) : IIntegrationEvent;
