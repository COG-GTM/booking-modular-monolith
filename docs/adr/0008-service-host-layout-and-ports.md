# 0008. Service host layout, ports and naming conventions

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0001](0001-service-boundaries.md), [0002](0002-strangler-fig-migration-behind-gateway.md),
  [0004](0004-data-ownership-database-per-service.md); implemented by AB-493, AB-502, AB-498, AB-496,
  AB-497 (Aspire), AB-503 (Docker/compose), AB-491 (Helm)

## Context

Several tickets build service hosts, compose files, Aspire wiring, Helm charts and tests in
parallel. They need one shared set of names and ports.

## Decision

**Project layout**

- Host: `src/Services/<Module>.Host/src/<Module>.Host.csproj`
- Tests: `src/Services/<Module>.Host/tests/<Module>.Host.Test`
- Gateway: `src/Gateway` (AB-489)
- Contracts: `src/Contracts/Contracts.Messages`, `src/Contracts/Contracts.Grpc`
- Each host composes its existing module from `src/Modules/<Module>` (module code is not moved),
  plus the BuildingBlocks libraries it needs.

**Ports**

| Component | Local HTTP | Local gRPC (HTTP/2 only) | Container |
| --- | --- | --- | --- |
| Gateway | 5000 | - | 80 |
| Identity service | 5100 | - | 80 |
| Flight service | 5200 | 5201 | 80 (+81 gRPC h2) |
| Passenger service | 5300 | 5301 | 80 (+81 gRPC h2) |
| Booking service | 5400 | - | 80 |
| Monolith (`src/Api`, unchanged) | 3001 (https 3000) | - | - |

**Naming**

- Service names (logs, traces, `MessageBroker:ServiceName`, queue prefixes, compose/Aspire
  resource names): `identity`, `flight`, `passenger`, `booking` (`<module>-service` for
  container/DNS names).
- Databases as in [0004](0004-data-ownership-database-per-service.md).
- Gateway clusters `identity|flight|passenger|booking`, destination `monolith`
  ([0002](0002-strangler-fig-migration-behind-gateway.md)).

## Alternatives considered

| Alternative | Pros | Cons | Why rejected |
| --- | --- | --- | --- |
| Move module code into each service folder | Self-contained services | Breaks the running monolith during transition | Monolith must keep building unchanged |
| Ad-hoc ports per ticket | No upfront decision | Collisions across parallel work | Parallel tickets need a fixed map |

## Consequences

- Parallel tickets can wire compose, Aspire, Helm and tests without coordination.
- Module code temporarily has two composition roots (monolith and service host).
