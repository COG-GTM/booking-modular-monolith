# Flight cut-over (step 2 of 4)

| | Monolith | Service |
| - | - | - |
| Upstream | `http://booking_modular_monolith:80` | `http://flight-service:80` (host 5200, gRPC 5201) |
| Postgres | `flight_modular_monolith` | `flight_service` |
| Mongo read | `flight_modular_monolith_read` | `flight_service_read` |
| Outbox/inbox | `flight_modular_monolith_persist_message` | `flight_service_persist_message` |
| Gateway routes | `/api/{v}/flight/**` | same |
| Sync edges | gRPC server for Booking (`Grpc:FlightAddress`) | same, port 81 in containers |

## Before the window

- `flight_service` migrated and on the same EF migration as `flight_modular_monolith`; Postgres has
  `max_prepared_transactions` > 0 (Flight uses `TransactionScope`).
- Seed data: flight-service seeds aircraft/airports on startup into its own DB; the copy truncates
  and replaces it with the monolith's rows, so ids stay identical.

## Flip

```bash
deployments/cutover/flip-route.sh flight service
```

During step 3 the monolith's Booking module is repointed (`Grpc__FlightAddress=http://flight-service:81`)
so bookings still in the monolith reserve seats in the service's data.

## Verify

1. `GET /api/v1.0/flight/get-available-flights` -> 200, gateway log `Proxying to http://flight-service:80`.
2. A flight created before the flip is returned by `GET /api/v1.0/flight/{id}` (read model copied).
3. Create a flight + seat; both appear in the read model within the outbox interval.
4. `POST /api/v1.0/booking` (Booking still in the monolith) reserves a seat in flight-service.

## Rollback

```bash
deployments/cutover/flip-route.sh flight monolith   # refused while booking is on the service
```

This is the validated reference rollback: `deployments/cutover/validate-rollback.sh`.
