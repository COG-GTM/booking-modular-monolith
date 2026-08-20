# Architecture Decision Records

ADRs documenting the decisions for migrating this modular monolith to microservices
(Phase 0 - Foundation, see [AB-230](https://cog-gtm.atlassian.net/browse/AB-230)).

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-service-boundaries.md) | Service boundaries: one service per existing module | Accepted |
| [0002](0002-strangler-fig-migration.md) | Strangler-fig migration behind an API gateway | Accepted |
| [0003](0003-inter-service-communication.md) | Inter-service communication: gRPC (sync) + RabbitMQ broker (async) | Accepted |
| [0004](0004-data-ownership.md) | Data ownership: database per service | Accepted |
| [0005](0005-contract-versioning.md) | Versioning and compatibility policy for contracts and protos | Accepted |

The target-state diagram lives in [docs/target-architecture.md](../target-architecture.md).

## Format

Each ADR follows the [Michael Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
**Status**, **Context**, **Decision**, **Consequences**. New ADRs get the next sequential
number; superseded ADRs are marked as such rather than deleted.
