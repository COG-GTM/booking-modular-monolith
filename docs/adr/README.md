# Architecture decisions — AB-499 / AB-488

These ADRs record the binding implementation direction supplied for AB-488, not a completed migration or ARB approval. Status stays **Proposed** until ARB sign-off. The starting point is the .NET 10 `src/Api` process composing the four modules under `src/Modules`; paths described as target paths are created by subsequent tickets.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-yarp-api-gateway-single-ingress.md) | YARP API gateway as the single ingress | Proposed |
| [0002](0002-synchronous-grpc-and-asynchronous-rabbitmq.md) | Synchronous gRPC and asynchronous RabbitMQ | Proposed |
| [0003](0003-service-owned-data-and-background-processing.md) | Service-owned data and background processing | Proposed |
| [0004](0004-contract-versioning-and-compatibility.md) | Contract versioning and compatibility | Proposed |
| [0005](0005-target-services-and-strangler-migration.md) | Target service boundaries and strangler migration | Proposed |
| [0100](0100-buildingblocks-shared-libraries.md) | Split BuildingBlocks into independently consumable shared libraries | Proposed |
| [0101](0101-versioned-integration-event-contracts.md) | Versioned integration-event contracts package | Proposed |
| [0102](0102-rabbitmq-transport-and-module-queues.md) | RabbitMQ transport, module-owned queues and broker-keyed inbox | Proposed |
| [0103](0103-shared-grpc-contracts-and-service-addressing.md) | Shared gRPC contracts and config/discovery-driven service addressing | Proposed |
| [0104](0104-passenger-service-host.md) | Standalone Passenger service host | Proposed |
| [0105](0105-standalone-identity-service-host.md) | Standalone Identity service host | Proposed |
| [0491](0491-kubernetes-helm-deployment.md) | Kubernetes Helm deployment for the services topology | Proposed |
| [0495](0495-per-service-observability-and-trace-propagation.md) | Per-service observability, probe contract and trace propagation | Proposed |
| [0496](0496-standalone-booking-service-host.md) | Standalone Booking service host | Proposed |
| [0497](0497-aspire-apphost-microservices-topology.md) | Aspire AppHost microservices topology | Proposed |
| [0502](0502-standalone-flight-service-host.md) | Standalone Flight service host | Proposed |
| [0503](0503-multi-service-docker-compose-topology.md) | Multi-service Docker Compose topology | Proposed |
| [0504](0504-contract-and-cross-service-testing.md) | Contract and cross-service testing | Proposed |
| [0505](0505-strangler-cutover-and-monolith-decommission.md) | Strangler cutover and monolith decommission | Proposed |

[Target container diagram](../../assets/target-microservices.svg) · [Mermaid source](0005-target-services-and-strangler-migration.md#architecture)

## Architecture review

ARB triage: **ARB_REQUIRED** (manual design review against the org trigger criteria).

- **T1:** four service hosts and a gateway become independently deployable (ADR 0005).
- **T2:** service-owned Postgres, persist-message, Mongo databases and Booking EventStoreDB ownership (ADR 0003).
- **T4:** formerly in-process messaging crosses process/domain boundaries through RabbitMQ; shared contracts/protos become independently consumed (ADRs 0002/0004).
- **T6:** gateway becomes public ingress; internal gRPC is temporarily unauthenticated (ADRs 0005/0002).
- **T8:** independently consumable BuildingBlocks and shared contracts (ADRs 0005/0004).
- **Not triggered:** T3 (no new vendor selected), T5 (no cloud resources/regions selected), T7 (.NET 10 retained). T9 is **undetermined**, not cleared: production sizing/cost is TBD.

**ARB ticket: TO BE CREATED** by the coordinating owner. Required reviewers: Architecture, Security, Data, Platform. This documentation does not grant an exception or approval to merge/deploy an architecture implementation. No infrastructure policy is present in the base repository; platform policy validation is still required when deployment resources are selected.

## Approval inputs still required

The coordinating owner must obtain named domain/platform owners and on-call rotations, measured availability/latency/load targets, RPO/RTO and retention/residency requirements, infrastructure sizing/monthly costs, and Security's disposition of the unauthenticated gRPC exception (scope, compensating controls, expiry). Unknowns are deliberately not represented as agreed numeric targets. Each implementation PR must supply its own test evidence and ARB linkage.

## Implementation checklist (target; not delivered on this branch)

- Gateway: `src/Gateway`; clusters `identity`, `flight`, `passenger`, `booking`, `monolith`; config-only destination changes.
- Hosts: `src/Services/<Module>.Host`, one Dockerfile each; self-compose only their module; tests under `src/Modules/<Module>/tests/`.
- Libraries: split `src/BuildingBlocks` into independently consumable projects, retaining thin `BuildingBlocks.csproj` and CLR namespaces. Events: `src/Contracts/Contracts.Messages`; protos: `src/Contracts`.
- Persistence: distinct credentials/databases per service, no monolith/host shared persist-message tables; migration/reconciliation/rollback evidence belongs to AB-505.
- Messaging: RabbitMQ outside tests; service-specific receive queues; no cross-domain direct database reads.
- Coexistence: disable the extracted module's monolith worker/subscription/consumers with `Modules:<Module>:BackgroundProcessingEnabled=false` (default `true`). Never start a second background processor against the same service stores.
- Compatibility: test mixed old/new consumers/providers, including real gRPC calls and RabbitMQ delivery; preserve message URNs and canonical gRPC service names.

To add a decision, use the next four-digit number, link related ADRs and this index, explain alternatives/consequences, and keep approval status separate from implementation status.
