# 0005. Side-by-side running: per-module `BackgroundProcessingEnabled` switch

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0002](0002-strangler-fig-migration-behind-gateway.md), [0004](0004-data-ownership-database-per-service.md)

## Context

During the strangler-fig transition the monolith and a module's new service host can run at the
same time (e.g. gateway already routes Flight traffic to the Flight service while the monolith is
still deployed for rollback). Request handling is controlled by the gateway, but **background work**
is not: outbox processing, Booking's EventStoreDB all-stream subscription (read-model projection)
and MassTransit consumers would run in both processes, producing duplicate publishes, duplicate
projections and split consumption.

## Decision

The monolith gets a **per-module switch**:

```json
"Modules": {
  "Identity":  { "BackgroundProcessingEnabled": true },
  "Flight":    { "BackgroundProcessingEnabled": true },
  "Passenger": { "BackgroundProcessingEnabled": true },
  "Booking":   { "BackgroundProcessingEnabled": true }
}
```

- Key: `Modules:<Module>:BackgroundProcessingEnabled` (env:
  `Modules__Flight__BackgroundProcessingEnabled=false`), **default `true`**, so the monolith behaves
  exactly as today unless configured.
- When `false`, the monolith does not start that module's background work: its outbox worker,
  (for Booking) the EventStoreDB subscription, and the module's MassTransit consumers. HTTP/gRPC
  endpoints stay mapped so rollback is a gateway change only.
- Operational rule: when a module's gateway cluster points at the service host, set the switch to
  `false` in the monolith; on rollback set it back to `true` (or stop the service host).

## Alternatives considered

| Alternative | Pros | Cons | Why rejected |
| --- | --- | --- | --- |
| Distributed locks / leader election on outbox | Both hosts can stay "on" | Complex, still double-projects read models | Overkill for a temporary state |
| Remove the module from the monolith when extracted | Clean | Rollback requires a monolith redeploy | Loses config-only rollback |

## Consequences

- Side-by-side operation is safe and reversible with config.
- One more operational step per extraction/rollback; documented in the cutover runbook (AB-505).
- The switch is transitional and is removed together with the monolith host.
