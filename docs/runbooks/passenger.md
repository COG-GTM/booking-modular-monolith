# Passenger cut-over (step 3 of 4)

| | Monolith | Service |
| - | - | - |
| Upstream | `http://booking_modular_monolith:80` | `http://passenger-service:80` (host 5300, gRPC 5301) |
| Postgres | `passenger_modular_monolith` | `passenger_service` |
| Mongo read | `passenger_modular_monolith_read` | `passenger_service_read` |
| Outbox/inbox | `passenger_modular_monolith_persist_message` | `passenger_service_persist_message` |
| Gateway routes | `/api/{v}/passenger/**` | same |
| Edges | consumes `UserCreated` (RabbitMQ); gRPC server for Booking | same |

## Before the window

- `passenger_service` migrated and on the same EF migration.
- While dark, passenger-service already consumes `UserCreated` from its own queue into
  `passenger_service`; the flip's copy replaces those rows with the monolith's (authoritative)
  ones and truncates the dark-run outbox/inbox rows.

## Flip

```bash
deployments/cutover/flip-route.sh passenger service
```

The monolith's consumer stops with its background processing (step 3); messages published during
the freeze queue up in RabbitMQ for passenger-service and are consumed once it acquires (step 5).
The monolith's Booking gRPC client is repointed to `http://passenger-service:81`.

## Verify

1. Register a user (Identity) -> `POST /api/v1.0/passenger/complete-registration` eventually 200,
   gateway log `Proxying to http://passenger-service:80`.
2. A passenger completed before the flip is returned by `GET /api/v1.0/passenger/{id}`.
3. `POST /api/v1.0/booking` (Booking still in the monolith) resolves the passenger over gRPC.

## Rollback

```bash
deployments/cutover/flip-route.sh passenger monolith   # refused while booking is on the service
```
