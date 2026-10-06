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
| [0006](0006-per-service-database-and-outbox-isolation.md) | Per-service database and outbox/inbox isolation | Proposed |
| [0007](0007-shared-grpc-contracts-and-service-addressing.md) | Shared, versioned gRPC contracts and discovery-based service addressing | Proposed |
| [0008](0008-api-gateway-single-ingress.md) | API Gateway (YARP) as the single ingress | Proposed |
| [0009](0009-standalone-identity-service-host.md) | Standalone Identity service host | Proposed |
| [0010](0010-standalone-flight-service-host.md) | Standalone Flight service host (first strangler-fig extraction) | Proposed |
| [0011](0011-standalone-passenger-service-host.md) | Standalone Passenger service host | Proposed |
| [0012](0012-standalone-booking-service-host.md) | Standalone Booking service host (strangler-fig extraction of the Booking module) | Proposed |
| [0013](0013-per-module-background-processing-switch.md) | Per-module background-processing switch in the monolith during cut-over | Proposed |
| [0014](0014-aspire-apphost-multi-service-topology.md) | Aspire AppHost orchestrates the gateway and four standalone services | Proposed |

The target-state diagram lives in [docs/target-architecture.md](../target-architecture.md).

## Format

Each ADR follows the [Michael Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
**Status**, **Context**, **Decision**, **Consequences**. New ADRs get the next sequential
number; superseded ADRs are marked as such rather than deleted.
