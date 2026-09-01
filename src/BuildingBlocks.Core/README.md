# BuildingBlocks.Core

Domain abstractions, CQRS interfaces, event primitives, and pagination helpers.

This is the foundational package that all other BuildingBlocks packages depend on.

## Contents

- `Core/` — IEventMapper, CompositeEventMapper, EventDispatcher, IEventDispatcher
- `Core/CQRS/` — ICommand, ICommandHandler, IQuery, IQueryHandler
- `Core/Event/` — IDomainEvent, IEvent, IIntegrationEvent, IInternalCommand, MessageEnvelope
- `Core/Model/` — Entity, Aggregate, IEntity, IAggregate
- `Core/Pagination/` — PageList, IPageList, IPageQuery, IPageRequest
- `Caching/` — CachingBehavior, InvalidateCachingBehavior
- `Constants/` — IdentityConstant
- `Exception/` — Custom exception types
- `Logging/` — LoggingBehavior
- `Validation/` — ValidationBehavior, ValidationError
- `Utils/` — ServiceLocator, TypeProvider
