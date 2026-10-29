# Cutover architecture — before, during, after

Operational entry point: [cutover runbooks](../runbooks/cutover/README.md).
Existing target: [target architecture](target-architecture.md). Arrows show HTTP routing, not
background ownership. All production data transitions require a write pause and reconciliation.

## Before (legacy production, not the default local Compose configuration)

```mermaid
flowchart LR
  C[Client] -->|HTTP JWT| G[YARP]
  G -->|all four clusters + fallback| M[Monolith]
  M --> P[(Legacy module PostgreSQL DBs + shared persist_message)]
  M --> R[(Mongo projections)]
  M --> E[(Booking EventStore + checkpoint)]
```

## During (Flight first, then Identity + Passenger, then Booking)

```mermaid
flowchart LR
  C[Client] -->|HTTP JWT| G[YARP]
  G -->|migrated clusters| S[Extracted services]
  G -->|remaining clusters + fallback| M[Monolith rollback target]
  S --> P[(Per-service PostgreSQL + message DBs)]
  M -->|same migrated stores on rollback| P
  S --> R[(Per-service Mongo projections)]
  S --> E[(Booking EventStore + checkpoint)]
  I[Identity] -->|AMQP UserCreated| Q[RabbitMQ]
  Q --> PAX[Passenger]
  B[Booking] -->|h2c gRPC unauthenticated| F[Flight]
  B -->|h2c gRPC unauthenticated| PAX
```

Only one Booking subscriber. Legacy consumer registration must be isolated for production
coexistence; module background flags alone do not isolate MassTransit consumers.

## After (current repository topology)

```mermaid
flowchart LR
  C[Client] -->|HTTP JWT| G[YARP, unknown paths return 404]
  G --> I[Identity]
  G --> F[Flight]
  G --> P[Passenger]
  G --> B[Booking]
  I -->|AMQP UserCreated| Q[RabbitMQ]
  Q --> P
  B -->|h2c gRPC| F
  B -->|h2c gRPC| P
  I --> D[(Per-service PostgreSQL / message stores)]
  F --> D
  P --> D
  B --> D
  F --> R[(Per-service Mongo)]
  P --> R
  B --> R
  B --> E[(EventStore, single active subscriber)]
```

No new infrastructure/vendor is introduced. Local HTTP/h2c is not production transport security.
The monolith host and fallback are retired. Recovery explicitly redeploys a previous-release
image using the rollback overlay; it is not part of normal startup.
