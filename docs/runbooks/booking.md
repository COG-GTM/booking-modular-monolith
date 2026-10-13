# Booking cut-over (step 4 of 4)

| | Monolith | Service |
| - | - | - |
| Upstream | `http://booking_modular_monolith:80` | `http://booking-service:80` (host 5400) |
| Write store | EventStoreDB (shared) | EventStoreDB (same store) |
| Mongo read | `booking_modular_monolith_read` | `booking_service_read` |
| Outbox/inbox | `booking_modular_monolith_persist_message` | `booking_service_persist_message` |
| Gateway routes | `/api/{v}/booking/**` | same |
| Sync edges | gRPC client of Flight + Passenger | `flight-service:81`, `passenger-service:81` |

## Before the window

- Flight and Passenger are already on their services (enforced by `flip-route.sh`).
- booking-service runs **exactly one replica** - `Modules__Booking__BackgroundProcessingEnabled`
  gates the ESDB `$all` subscription that projects into Mongo, which must have a single owner.

## Flip

```bash
deployments/cutover/flip-route.sh booking service
```

Booking's event store is not migrated: both hosts read and append the same streams and share the
subscription checkpoint stream `checkpoint_default`. With the monolith's subscription stopped
(step 3), the Mongo read model is copied to `booking_service_read`; booking-service's
subscription resumes from the shared checkpoint, so nothing is projected twice or skipped.

Production note: when Booking later gets its own EventStoreDB, migrate with a replicator
(stream-by-stream copy preserving event ids) during a freeze and reset the checkpoint to the new
store's `$all` position after the last copied event.

## Verify

1. `POST /api/v1.0/booking` -> 200 via `booking-service`; seat reserved in flight-service.
2. A booking created before the flip is still projected (`booking_service_read`).
3. `deployments/docker-compose/smoke-test.sh` passes (full path, every context on services).

## Rollback

```bash
deployments/cutover/flip-route.sh booking monolith
```

The monolith's subscription resumes from the same checkpoint and its read model is replaced
with `booking_service_read`.
