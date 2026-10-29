# Architecture Decision Records

This folder records the decisions behind epic **AB-488 — Microservices Migration Scaffolding**: moving
`booking-modular-monolith` from one deployable (the now-retired monolith host) to one service per module, without a
big-bang rewrite.

The target state is drawn in [docs/architecture/target-architecture.md](../architecture/target-architecture.md).

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-service-boundaries.md) | One service per existing module: Identity, Flight, Passenger, Booking | Proposed |
| [0002](0002-strangler-fig-migration-behind-yarp-gateway.md) | Strangler-fig migration behind a YARP gateway | Proposed |
| [0003](0003-sync-grpc-async-rabbitmq.md) | gRPC for synchronous calls, RabbitMQ (MassTransit) for integration events | Proposed |
| [0004](0004-database-per-service-and-per-service-outbox-inbox.md) | Database per service, with a per-service outbox and inbox | Proposed |
| [0005](0005-contract-and-proto-versioning-policy.md) | Versioning and compatibility policy for integration events and protos | Proposed |
| [0006](0006-repository-layout-for-hosts-and-contracts.md) | Repository layout for the gateway, service hosts, contracts and BuildingBlocks | Proposed |
| [0007](0007-extraction-order.md) | Extraction order: Flight, then Passenger + Identity, then Booking | Proposed |
| [0008](0008-defer-service-to-service-grpc-authentication.md) | Defer authentication of service-to-service gRPC | Proposed |
| 0009 | Reserved for AB-499 (unused) | — |
| 0010 | BuildingBlocks package boundaries — added by AB-500 | Lands in a sibling PR |
| [0011](0011-service-artifact-publishing.md) | Independent service artifact publishing (AB-490) | Proposed |
| [0013](0013-identity-service-token-issuer.md) | Identity service is the sole token issuer behind the gateway (AB-493) | Proposed |
| [0014](0014-grpc-contracts-package-and-service-addressing.md) | Shared gRPC contracts and service addressing (AB-494) | Proposed |
| [0016](0016-booking-service-host.md) | Booking service host (AB-496) | Proposed |
| [0018](0018-passenger-service-host.md) | Passenger service host (AB-498) | Proposed |
| [0021](0021-rabbitmq-delivery-and-messageid-inbox.md) | RabbitMQ delivery and MessageId-based endpoint inbox (AB-501) | Proposed |
| [0022](0022-flight-service-host.md) | Flight service host (AB-502) | Proposed |
| [0025](0025-multi-service-docker-compose-topology.md) | Multi-service Docker Compose topology (AB-503) | Proposed |
| [0023](0023-per-service-observability.md) | Per-service observability (AB-495) | Proposed |
| [0027](0027-cutover-and-monolith-decommission.md) | Cutover and monolith decommission (AB-505) | Proposed |

## Conventions

- File name: `NNNN-<kebab-case-title>.md`, numbered in sequence. Numbers are never reused.
- Numbers for epic AB-488: 0001–0009 belong to AB-499 (architecture), 0010 to AB-500 (BuildingBlocks
  package boundaries), 0011 to AB-490 (CI/CD and package publishing), 0016 to AB-496 (Booking
  host), 0018 to AB-498 (Passenger host), 0022 to AB-502 (Flight host), 0023 to AB-495
  (per-service observability), and 0025 to AB-503 (multi-service compose).
- Every ADR uses the org ADR template (Context, Decision, Alternatives, Architecture, NFRs, Security,
  Cost, Operations, Policy exceptions, Consequences, Open questions). A section that does not apply
  says `N/A — <reason>` instead of being removed.
- New ADRs start as `Proposed`. Only the Architecture Review Board (ARB) moves an ADR to `Accepted`.
- A decision is changed by writing a new ADR that supersedes the old one. The old ADR gets
  `Superseded by NNNN` in its status line and is otherwise left as written.
- Numbers that are not known yet (SLOs, load, cost) are written as `TBD — owner to confirm before
  ARB` and repeated under *Open questions*. Do not guess them.

## ARB

The epic adds deployable services (T1), new data stores (T2), a new broker integration between
bounded contexts (T4), gateway and token-validation changes (T6) and shared contract libraries
(T8), so these ADRs form its ARB submission. The ARB ticket is **TO BE CREATED** (there is no ARB
project on the Jira site yet). Implementation PRs reference the ADR they implement; merging the
epic branch into `main` waits on ARB approval.

- [0012: Per-service data stores](0012-per-service-data-stores.md)
- [0016: Booking service host](0016-booking-service-host.md)
- [0018: Passenger service host](0018-passenger-service-host.md)
- [0022: Flight service host](0022-flight-service-host.md)
- [0023: Per-service observability](0023-per-service-observability.md)
- [0025: Multi-service Docker Compose topology](0025-multi-service-docker-compose-topology.md)
