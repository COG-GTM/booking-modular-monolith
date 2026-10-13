# Architecture Decision Records

Decisions for migrating the booking modular monolith to microservices
(epic [AB-488](https://cog-gtm.atlassian.net/browse/AB-488), Phase 0 - Foundation:
[AB-499](https://cog-gtm.atlassian.net/browse/AB-499)).

The target-state diagram and the migration phases are in
[docs/architecture/target-architecture.md](../architecture/target-architecture.md).

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-service-boundaries.md) | Service boundaries: one service per existing module | Proposed |
| [0002](0002-strangler-fig-migration-behind-gateway.md) | Strangler-fig migration behind a YARP gateway | Proposed |
| [0003](0003-inter-service-communication.md) | Inter-service communication: gRPC (sync) + RabbitMQ via MassTransit (async) | Proposed |
| [0004](0004-data-ownership-database-per-service.md) | Data ownership: database per service, per-service outbox/inbox | Proposed |
| [0005](0005-side-by-side-background-processing-switch.md) | Side-by-side running: per-module `BackgroundProcessingEnabled` switch | Proposed |
| [0006](0006-contract-and-proto-versioning.md) | Versioning and compatibility policy for contracts and protos | Proposed |
| [0007](0007-unauthenticated-internal-grpc-accepted-risk.md) | Unauthenticated service-to-service gRPC (accepted risk, ARB follow-up) | Proposed |
| [0008](0008-service-host-layout-and-ports.md) | Service host layout, ports and naming conventions | Proposed |
| [0495](0495-ab-495-per-service-observability-and-health.md) | Per-service observability, health probes and trace propagation (AB-495) | Proposed |

Status moves from `Proposed` to `Accepted` once the ARB ticket is approved
(ARB ticket: TO BE CREATED - there is no ARB project on the Jira site yet).

## Numbering

- `0001`-`0009` are reserved for the foundation decisions above (AB-499).
- Ticket-specific ADRs written by later tickets of the epic are named
  `NNNN-ab-<ticket>-<slug>.md` (e.g. `0010-ab-489-api-gateway.md`) so parallel work never
  collides on a number.
- Superseded ADRs are marked `Superseded by NNNN`, never deleted.

## Format

Each ADR records **Status/metadata**, **Context**, **Decision**, **Alternatives considered** and
**Consequences**. [0001](0001-service-boundaries.md) is the umbrella ADR and additionally carries the
org ARB sections (architecture diagram, NFRs, security, cost, operations).
