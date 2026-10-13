# Strangler-fig cut-over runbook (AB-505)

This runbook moves traffic for the four bounded contexts from the monolith host to the standalone
service hosts, one context at a time, behind the YARP gateway, and then retires the monolith host.
Every step before decommission is reversible.

| Context   | Runbook                        | Service upstream               | Writes                         | Read model                 |
| --------- | ------------------------------ | ------------------------------ | ------------------------------ | -------------------------- |
| Identity  | [identity.md](identity.md)     | `http://identity-service:80`   | Postgres `identity_service`    | -                          |
| Flight    | [flight.md](flight.md)         | `http://flight-service:80`     | Postgres `flight_service`      | Mongo `flight_service_read` |
| Passenger | [passenger.md](passenger.md)   | `http://passenger-service:80`  | Postgres `passenger_service`   | Mongo `passenger_service_read` |
| Booking   | [booking.md](booking.md)       | `http://booking-service:80`    | EventStoreDB (shared)          | Mongo `booking_service_read` |
| (host)    | [decommission.md](decommission.md) | -                          | -                              | -                          |

Scripts live in [`deployments/cutover/`](../../deployments/cutover); the target picture is in
[`docs/architecture/target-architecture.md`](../architecture/target-architecture.md) and the
decision record in [ADR 0505](../adr/0505-ab-505-cutover-rollback-and-monolith-decommission.md).

## 1. Cut-over order

**Identity -> Flight -> Passenger -> Booking**, one context per change window, with a burn-in
period (at least one business day without a rollback trigger, see section 6) between flips.

| # | Context   | Why here                                                                                                                                                                                                                                                 |
| - | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1 | Identity  | Every other route depends on the tokens it issues. Moving it first exercises token continuity (shared signing key + gateway-pinned issuer) while all business routes still sit on the monolith, so a problem shows up as a clean 401 rather than corrupt state. Its only async edge (`UserCreated` -> Passenger) already crosses RabbitMQ. |
| 2 | Flight    | Leaf context: no outbound calls. Booking (still in the monolith) is repointed to `flight-service:81` for gRPC as part of the flip. It is also the rollback reference context (section 5).                                                                |
| 3 | Passenger | Same shape as Flight (gRPC server for Booking, consumer of Identity's `UserCreated`), done once both of its producers are on services.                                                                                                                    |
| 4 | Booking   | Depends on Flight and Passenger over gRPC; `booking-service` is wired to `flight-service:81`/`passenger-service:81`, so it can only move after both. `flip-route.sh` enforces this (and the reverse order for rollback).                                  |

ADR 0002 sketched Flight first; we keep the ticket's Identity-first order because the token issuer
is the one dependency every other context's verification relies on, and moving it first while it
is the only thing that moved keeps its blast radius small and its rollback config-only.

## 2. How a route flip works

The gateway has one YARP cluster per context, each with a single destination named `monolith`.
A flip only changes that destination's address:

```
ReverseProxy__Clusters__<identity|flight|passenger|booking>__Destinations__monolith__Address
  monolith : http://booking_modular_monolith:80
  service  : http://<context>-service:80
```

Ownership of a context's *background* work moves with the route. Exactly one process may run it:

```
monolith : Modules__<Module>__BackgroundProcessingEnabled   (src/Api, default true)
service  : Modules__<Module>__BackgroundProcessingEnabled   (<Module>.Host; false while dark)
```

`deployments/cutover/flip-route.sh <context> service|monolith` performs the hand-over:

1. **Freeze** - the context's cluster is pointed at an unroutable upstream; clients get `502`
   for that context only (the other three keep working). Typical window: 1-2 minutes.
2. **Drain** - wait until the current owner's `*_persist_message` DB has no unprocessed
   outbox/inbox/internal-command rows (`message_status <> 2`).
3. **Release** - turn the current owner's background processing off. For Flight/Passenger the
   monolith's Booking gRPC client (`Grpc:FlightAddress`/`Grpc:PassengerAddress`) follows the move.
4. **Copy** - `copy-data.sh <context> to-service|to-monolith` (section 3).
5. **Acquire** - turn the new owner's background processing on.
6. **Route** - point the cluster at the new owner.
7. **Verify** - a request through the gateway must be answered by the new upstream (gateway
   `Proxying to http://<upstream>` log line), then run the context's smoke checks from its runbook.

State is kept in `deployments/cutover/.state/routes.env` and fed to compose with `--env-file`;
`flip-route.sh status` prints it. Re-running a flip after a failure resumes it (every step is
idempotent). `DRY_RUN=1` prints the plan without touching anything. In Kubernetes or any other
orchestrator the same seven steps apply with the env vars set on the gateway/host deployments.

## 3. Data migration / backfill

Each service owns new databases (`*_service`), separate from the monolith's `*_modular_monolith`
databases. Data moves **at the flip, inside the write freeze** (an "offline copy" per context):

| Store                       | Identity | Flight | Passenger | Booking | Method                                                        |
| --------------------------- | :------: | :----: | :-------: | :-----: | ------------------------------------------------------------- |
| Postgres write DB           |    x     |   x    |     x     |    -    | truncate target, `pg_dump --data-only` -> `psql`, row counts   |
| Mongo read DB               |    -     |   x    |     x     |    x    | drop target, `mongodump` -> `mongorestore --nsFrom/--nsTo`, document counts |
| `*_persist_message`         |  reset   | reset  |   reset   |  reset  | source drained first; target truncated (stale dark-run rows)   |
| EventStoreDB                |    -     |   -    |     -     | shared  | not copied - both hosts use the same store and checkpoint stream |
| Identity signing key        |  shared  |   -    |     -     |    -    | shared volume / secret, see identity.md                        |

Rules:

- **Schema is never copied.** Each host applies its own EF Core migrations at startup;
  `copy-data.sh` refuses to run if the two databases are on different `__EFMigrationsHistory`
  heads. Deploy the service build that matches the monolith's migration set before the window.
- **Verification is mandatory.** The copy fails (and the flip stops while still frozen) if any
  table's row count or any collection's document count differs. For production, additionally
  compare `md5(string_agg(t::text, '' order by id))` per table on both sides for the largest tables.
- **Large tables.** The offline copy is sized for this system's data volumes (seconds). If a
  context's freeze would exceed the agreed window, pre-seed the target with a logical
  replication subscription (`CREATE PUBLICATION ... FOR ALL TABLES` on the monolith DB) during
  the dark period and make the freeze only wait for replication lag 0 before step 5 - the copy
  step then degrades to verification (`copy-data.sh <ctx> to-service --verify-only`).
- **The old store is kept read-only** (not dropped) until decommission + retention period; it is
  what a rollback copies back into.

## 4. Canary and dark launch

**Dark launch (default, always on before a flip).** Service hosts run in production next to the
monolith with background processing off, on their own (empty or pre-seeded) databases. They
receive no gateway traffic, but are health-checked, scraped and can be exercised by synthetic
checks directly on `:5100/:5200/:5300/:5400`. Note that dark Passenger still consumes
`UserCreated` from RabbitMQ into its own DB; that is harmless because the flip's copy replaces it.

**Header canary (internal users / synthetic traffic).** Add a higher-priority route that matches a
header and points at a second cluster for the service:

```jsonc
"Routes": {
  "flight-canary": {
    "ClusterId": "flight-service",
    "Order": -1,
    "Match": { "Path": "/api/{version}/flight/{**catch-all}",
               "Headers": [ { "Name": "X-Cutover-Canary", "Values": [ "flight" ] } ] }
  }
},
"Clusters": {
  "flight-service": { "Destinations": { "service": { "Address": "http://flight-service:80" } } }
}
```

**Weighted canary.** YARP has no weighted load-balancing policy built in; add the service as a
second destination of the context's cluster (`Destinations__canary__Address`) with
`LoadBalancingPolicy: RoundRobin` for a 50/50 split, or duplicate the monolith destination to
get 1/3, 1/4, ... shares. Use `SessionAffinity` (cookie) so a user stays on one side.

**Only canary reads, or canary while stores are shared.** A canary that sends writes to both
hosts while each owns a different database splits the data and makes rollback lossy. Canary
write traffic only when the service is temporarily pointed at the monolith's databases
(`PostgresOptions__ConnectionString__<M>` = monolith DB) and only one side runs background
processing; otherwise restrict the canary to GET routes.

## 5. Rollback

Every flip has the same rollback: `flip-route.sh <context> monolith`. It runs the same seven
steps in reverse direction - freeze, drain the service's outbox, turn the service's background
work off, copy the service DBs back into the monolith DBs (so writes accepted by the service are
kept), turn the monolith's background work on, route back, verify.

- Order matters: roll Booking back before Flight or Passenger (enforced by the script).
- Rolling back Identity does not invalidate issued tokens (shared key, same issuer).
- After decommission there is no monolith to roll back to; see [decommission.md](decommission.md).

Validated on the compose topology with `deployments/cutover/validate-rollback.sh` (Flight: write
on monolith -> flip -> read + write on service -> roll back -> both writes readable on monolith).

## 6. Monitoring and rollback triggers

Watch for 30 minutes after each flip (Grafana / Jaeger from the compose stack, `service.name` per
host), then daily during burn-in. Roll back if any of these hold:

| Signal                                                          | Trigger                                        |
| --------------------------------------------------------------- | ---------------------------------------------- |
| Gateway 5xx rate for the context's routes                        | > 1 % over 5 min, or any sustained 502/503      |
| p95 latency of the context's routes through the gateway          | > 2x the monolith baseline over 15 min          |
| `/health/ready` on the service host                              | failing for > 2 min                             |
| Unprocessed `*_service_persist_message` rows                     | growing for > 10 min                            |
| RabbitMQ queue depth / `_error` queues for the service           | growing, or any message in an `_error` queue    |
| Booking only: projection lag (ESDB position vs. checkpoint)      | > 1 min                                         |

## 7. Running it locally

```bash
# src/Api is decommissioned: replaying the cut-over needs the last released monolith image.
export MONOLITH_IMAGE=ghcr.io/cog-gtm/booking-monolith:<last-version>
export COMPOSE_DIR=deployments/docker-compose
docker compose -f $COMPOSE_DIR/docker-compose.yaml -f deployments/cutover/docker-compose.cutover.yaml build
deployments/cutover/flip-route.sh up          # all contexts on the monolith, services dark
deployments/cutover/validate-rollback.sh      # flip flight -> service -> monolith with data checks
deployments/cutover/flip-route.sh identity service
deployments/cutover/flip-route.sh flight service
deployments/cutover/flip-route.sh passenger service
deployments/cutover/flip-route.sh booking service
deployments/docker-compose/smoke-test.sh      # end-to-end through the gateway, all on services
deployments/cutover/flip-route.sh status
```
