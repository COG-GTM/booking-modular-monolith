# Strangler-fig cut-over, rollback and monolith decommission runbook

- **Ticket:** [AB-248](https://cog-gtm.atlassian.net/browse/AB-248) (epic [AB-231](https://cog-gtm.atlassian.net/browse/AB-231))
- **Decision record:** [ADR 0016](../adr/0016-cutover-order-route-flip-and-decommission.md)
- **Related:** [ADR 0002](../adr/0002-strangler-fig-migration.md) (strangler fig),
  [ADR 0006](../adr/0006-per-service-database-and-outbox-isolation.md) (per-service DB + outbox/inbox),
  [ADR 0008](../adr/0008-api-gateway-single-ingress.md) (gateway),
  [ADR 0013](../adr/0013-per-module-background-processing-switch.md) (background-processing switch),
  [ADR 0014](../adr/0014-multi-service-docker-compose-topology.md) (compose topology),
  [ADR 0015](../adr/0015-aspire-apphost-multi-service-topology.md) (Aspire)
- **Tooling:** [`deployments/scripts/route-flip.sh`](../../deployments/scripts/route-flip.sh),
  [`deployments/scripts/validate-rollback.sh`](../../deployments/scripts/validate-rollback.sh),
  [`deployments/scripts/decommission-monolith.sh`](../../deployments/scripts/decommission-monolith.sh),
  [`docker-compose.cutover.yaml`](../../deployments/docker-compose/docker-compose.cutover.yaml),
  [`docker-compose.phase-a.yaml`](../../deployments/docker-compose/docker-compose.phase-a.yaml)

This runbook moves client traffic from the modular monolith (`src/Api`) to the standalone services
one bounded context at a time, behind the YARP gateway (`src/Gateway`, port 5000). Every step is a
configuration change and has a rollback that is the same change in reverse.

## 1. How a route flip works

The gateway has one cluster per context (`identity`, `flight`, `passenger`, `booking`) plus a
`monolith` fallback cluster. Each cluster has one destination, named `monolith` for historical
reasons. A **flip** repoints that destination:

| Context | Gateway routes (cluster) | Monolith address | Service address |
| --- | --- | --- | --- |
| identity | `/api/{version}/identity/**`, `/connect/**`, `/.well-known/**` | `http://booking_modular_monolith:80` | `http://identity-service:80` |
| flight | `/api/{version}/flight/**` | `http://booking_modular_monolith:80` | `http://flight-service:80` |
| passenger | `/api/{version}/passenger/**` | `http://booking_modular_monolith:80` | `http://passenger-service:5102` |
| booking | `/api/{version}/booking/**` | `http://booking_modular_monolith:80` | `http://booking-service:80` |

The setting is `ReverseProxy__Clusters__<ctx>__Destinations__monolith__Address`. YARP reads it at
start-up, so a flip restarts (compose) or rolls (Kubernetes) the gateway only; services and the
monolith keep running. Under Aspire, use `Gateway__Clusters__<ctx>` on the AppHost instead
(ADR 0015).

`route-flip.sh` wraps this:

```bash
# Compose (topology = docker-compose.services.yaml + docker-compose.phase-a.yaml + docker-compose.cutover.yaml)
deployments/scripts/route-flip.sh flight service --dry-run   # show the change only
deployments/scripts/route-flip.sh flight service             # flip, then health + smoke check
deployments/scripts/route-flip.sh flight monolith            # rollback
deployments/scripts/route-flip.sh status                     # where every cluster points now

# Kubernetes (gateway Deployment named "gateway"; kubectl set env + rollout status)
deployments/scripts/route-flip.sh flight service --target k8s --namespace booking \
  --gateway-url https://api.example.com
```

After each flip the script waits for `GET /health` = 200 on the gateway, gets a token through
`/connect/token` (credentials from `SMOKE_USERNAME`, `SMOKE_PASSWORD`, `SMOKE_CLIENT_ID`,
`SMOKE_CLIENT_SECRET` — no defaults; the local dev values are the seeded `samh` user and the
`client`/`secret` client from `booking.rest`), calls a context endpoint through the gateway with
that token and checks that the `X-Gateway-Upstream` response header names the expected
destination. A response without the header **fails** the check (the destination is unproven)
unless you pass `--allow-unverified-upstream`; a failed smoke check exits non-zero and prints the
rollback command.

`X-Gateway-Upstream` (host:port of the destination that served the response) is **off by
default** because it discloses internal host names, and even when enabled it is only added to
responses for authenticated callers (`UpstreamHeaderOptions:AuthenticatedOnly`, default `true`), so
anonymous internet traffic never sees it. Enable it for the cut-over window
(`UpstreamHeaderOptions__Enabled=true`) and turn it off afterwards. The gateway always logs
`Proxying to <destination>` per request (`Yarp.ReverseProxy.Forwarder.HttpForwarder`), which is the
production signal.

**Compose route state.** `route-flip.sh` writes the chosen upstream per cluster
(`GATEWAY_<CTX>_UPSTREAM=...`) to `deployments/docker-compose/.env` (git-ignored). Docker Compose
loads that file automatically for every command run against this compose project, so a later plain
`docker compose ... up -d` (a redeploy, a rebuilt image) keeps the routes exactly as last flipped
instead of resetting them to the overlay defaults (every cluster on its service). To start from a
clean state delete the file; to keep it elsewhere set `ROUTE_FLIP_STATE_FILE` and pass
`--env-file` to your own compose commands.

## 2. Cut-over order

**Identity → Flight → Passenger → Booking.** This replaces the provisional order in ADR 0002
(Flight → Passenger → Booking → Identity); see ADR 0016.

| # | Context | Why here |
| --- | --- | --- |
| 1 | **Identity** | Every other flip's smoke check needs a token. Identity-service is already the issuer every service validates against (`Jwt__MetadataAddress=http://identity-service/...`), so flipping its routes first makes the token issuer and validator the same process for the rest of the cut-over. It publishes `UserCreated` to RabbitMQ, which Passenger consumes; flipping the publisher before the consumer means Passenger's flip later does not change who produces its input. |
| 2 | **Flight** | Leaf service: no outbound gRPC, only consumed (Booking → Flight `ReserveSeat`/`GetFlightById` over gRPC). Owns its own `flight_service` database, so it is the context that needs a real backfill (section 4) and is the one we rehearse rollback on (section 7). |
| 3 | **Passenger** | Consumes Identity's `UserCreated` (already moved in step 1); consumed by Booking over gRPC. |
| 4 | **Booking** | Calls Flight **and** Passenger over gRPC and owns the EventStoreDB streams. Flip last, once both of its dependencies are served by services and burned in, so a Booking incident can't be caused by a half-migrated dependency. |

One flip per change window. Do not start the next context until the previous one has passed its
burn-in (default 7 days without rollback, error budget intact).

## 3. Pre-flight checks (every flip)

1. **Service is healthy:** `GET http://<service>/ready` = 200 (checks its Postgres/Mongo/RabbitMQ/EventStore), and `GET /alive` = 200.
2. **Gateway is healthy:** `GET http://gateway/health` = 200; `route-flip.sh status` shows the expected current state.
3. **Tokens are interchangeable:** a token from `POST /connect/token` through the gateway is accepted by both the monolith and the service (call one endpoint on each directly with the same bearer token). For the identity flip also compare the signing keys: `curl -s http://identity-service/.well-known/openid-configuration/jwks` and the monolith's must contain the same `kid`, otherwise tokens issued by one are rejected by the other's validators after a rollback. `AuthOptions:SigningKeyPath` makes an Identity host load/persist its signing key from a given file and turns off Duende's automatic key management (otherwise every host adds its own generated key and the JWKS differ). On the first start with the setting, a host whose target file does not exist yet copies the key it has been signing with (its content-root `tempkey.jwk`) to that path, so tokens issued before the switch keep validating — seed the shared location from the *currently active* issuer (start it first) and verify a pre-switch token afterwards: `docker-compose.cutover.yaml` points both hosts at `/keys/identity/tempkey.jwk` on the shared `identity-signing-keys` volume, and `validate-rollback.sh identity` fails if the two JWKS documents differ. In Kubernetes mount the same secret/PVC into both deployments (or move both to a shared Duende key store); the developer credential is a stop-gap for the cut-over window, not a production key.
4. **Background-processing ownership (ADR 0013):** exactly one process runs each module's outbox/inbox poller, consumers and projections. Before flipping a context whose service shares the monolith's store, set `Modules__<Module>__BackgroundProcessingEnabled=false` on the monolith and restart it; the service keeps it `true`. Check the monolith's start-up warning (`Background processing is disabled for module <Module>`).
5. **Outbox/inbox drained** in the store being handed over (section 4): `select count(*) from persist_message where message_status <> 'Processed'` (adjust to the module schema) is 0, or stable and owned by the process that will keep running.
6. **One store per context (Phase A)** or **data backfilled and verified** (Phase B, section 4) — never a service and the monolith writing different stores for the same context.
7. **gRPC addressing for dependants:** Booking's `Services__flight__http__0` / `Services__passenger__http__0` point at the services (they already do in `docker-compose.services.yaml`).
8. **Dashboards open:** gateway and target service (section 6). Announce the window; name the person who will run the rollback.
9. **Dry run:** `route-flip.sh <ctx> service --dry-run` prints exactly the change you expect.

## 4. Data migration / backfill

Where each context's data lives today (`deployments/docker-compose/init/postgres/01-service-databases.sql`
creates one role per module; a role owns only its module's databases):

| Context | Monolith store | Service store (as configured) | State |
| --- | --- | --- | --- |
| identity | Postgres `identity_modular_monolith` | same database | **Shared store** – no data move at flip time |
| passenger | Postgres `passenger_modular_monolith`, Mongo `passenger_modular_monolith_read` | same | **Shared store** |
| booking | Postgres `booking_modular_monolith` (outbox/inbox), Mongo `booking_modular_monolith_read`, EventStoreDB streams | same | **Shared store** |
| flight | Postgres `flight_modular_monolith`, Mongo `flight_modular_monolith_read` | Postgres `flight_service`, Mongo `flight_service_read` by default; the cut-over overlay points flight-service at the monolith's stores | **Shared store in the overlay**; separate store = Phase B |

Two-phase approach for every context:

**Phase A – move the code, keep the data (flip-safe).** The service connects to the monolith's
module database (same EF Core migrations: both hosts compose the same `src/Modules/<Module>`
project) and the monolith's background processing for that module is switched off. A route flip
in either direction then moves no data and loses no writes; rollback is instant. Identity,
Passenger and Booking are already configured this way, and `docker-compose.phase-a.yaml` does the
same for Flight (`PostgresOptions__ConnectionString__Flight`, `MongoOptions__Flight__DatabaseName`
→ `flight_modular_monolith` / `flight_modular_monolith_read`, monolith Flight background processing
off). This matters beyond the flipped route itself: while Booking is still served by the monolith,
its Booking module reserves seats through *its own* Flight module over gRPC (`Services__flight__http__0`
→ `localhost:81` in the overlay), so Flight data written through flight-service and Flight data read
by monolith Booking must be the same rows. Running Flight on a separate store before Booking has
moved would make new flights invisible to monolith bookings. Do Phase B for Flight only after the
Booking flip, or re-point the monolith's `Services__flight__http__0` at `flight-service:81` together
with the Flight flip (and back on rollback).

**Phase B – separate the data (after burn-in, per context).** The target is the service's own,
never-used database (during Phase A the service writes to the shared store, so nothing written through
the service is lost by the copy); never restore over a database that has served traffic. Done in a
short write freeze, after
the route has been on the service long enough that rolling back to the monolith is no longer
planned:

1. **Drain the outbox/inbox first.** Make the service the only processor (ADR 0013), wait until
   `persist_message` has no unprocessed rows and RabbitMQ queues for the module are empty
   (`rabbitmqctl list_queues name messages | grep -i <module>`). Undrained outbox rows would be
   copied and published again from the new database; unprocessed inbox rows would be lost.
2. **Freeze writes:** keep the route on the service and stop it (or return 503 from a maintenance
   route) for the copy window. Reads can continue from the old store.
3. **Copy Postgres** (same schema, same migrations history):
   ```bash
   pg_dump -h postgres -U flight -d flight_modular_monolith -Fc -f flight.dump
   pg_restore -h postgres -U flight -d flight_service --clean --if-exists flight.dump
   ```
4. **Rebuild or copy the Mongo read model:** `mongodump --db flight_modular_monolith_read` →
   `mongorestore --nsFrom 'flight_modular_monolith_read.*' --nsTo 'flight_service_read.*'`, or
   let the service rebuild it from its internal commands. EventStoreDB streams (Booking) are not
   copied; the Booking service keeps the same event store and its own checkpoint.
5. **Verify:** row counts per table and a checksum of key columns match
   (`select count(*), md5(string_agg(id::text, ',' order by id)) from flight`); spot-check by id
   through the gateway.
6. **Repoint the service** to the new database (`PostgresOptions__ConnectionString__<Module>`),
   restart it, unfreeze, run the smoke check.
7. **Revoke** the service role's access to the old database; keep the old database read-only for
   the retention period, then drop it as part of decommission (section 8).

Rollback of Phase B before step 6: discard the copy, nothing changed. After step 6 with new
writes: reverse the copy (new → old) in a freeze, then flip the route back.

## 5. Canary / dark launch

The default flip is all-or-nothing per context. For high-risk contexts (Booking) use a canary
first; all of these are YARP configuration only:

- **Weighted canary:** add a second destination to the cluster and use a weighted load-balancing
  policy (YARP's built-in policies are not weighted; use `PowerOfTwoChoices`/`RoundRobin` across
  N destinations entries to approximate 1/N, or a custom `ILoadBalancingPolicy` reading
  destination `Metadata.Weight`). Example (50/50):
  ```bash
  ReverseProxy__Clusters__booking__LoadBalancingPolicy=RoundRobin
  ReverseProxy__Clusters__booking__Destinations__monolith__Address=http://booking_modular_monolith:80
  ReverseProxy__Clusters__booking__Destinations__service__Address=http://booking-service:80
  ```
  Only canary contexts in **Phase A** (shared store); with separate stores a canary splits
  writes across two databases.
- **Header-based dark launch:** add a route with a higher priority (lower `Order`) that matches a
  header and targets a new cluster pointing at the service, so testers/synthetics opt in:
  ```json
  "booking-canary": {
    "ClusterId": "booking-service", "Order": -1,
    "AuthorizationPolicy": "ApiScope", "RateLimiterPolicy": "ingress",
    "Match": { "Path": "/api/{version}/booking/{**catch-all}",
               "Headers": [ { "Name": "X-Canary", "Values": [ "booking-service" ] } ] }
  }
  ```
  with cluster `booking-service` → `http://booking-service:80`. Remove the route after the flip.
- **Shadow reads** (optional): run the same read-only smoke requests against the service port
  directly and compare payloads with the monolith before flipping.

Enable `UpstreamHeaderOptions__Enabled=true` during canaries to see per-response which side served.

## 6. Monitoring signals

Watch for at least 30 minutes after each flip, then daily during burn-in (OTel → Prometheus /
Grafana / Jaeger, `deployments/configs/dashboards.md`):

| Signal | Where | Roll back if |
| --- | --- | --- |
| Gateway 5xx rate per route (`http.server.request.duration` count by `http.route`, status) | gateway metrics | > 1% for 5 min, or any sustained 502/503 (destination down) |
| Gateway p95 latency per route | gateway metrics | > 2x pre-flip baseline for 10 min |
| 401/403 rate | gateway | any step change (token/issuer/key mismatch) |
| Service `/ready` | probes | not 200 |
| Outbox backlog / oldest unprocessed `persist_message` | service Postgres | growing for 10 min |
| RabbitMQ queue depth for the module's consumers, dead-letter/`_error` queues | RabbitMQ management (15672) | growing / any new error messages |
| gRPC errors Booking → Flight/Passenger | Booking traces/metrics | any `Unavailable`/`DeadlineExceeded` increase |
| Read-model lag (Mongo document count vs Postgres rows) | service | diverging |
| Business smoke: create booking end-to-end (`src/Tests/CrossService.EndToEnd.Test`) | CI / synthetic | failing |

## 7. Rollback (every flip)

Rollback = point the cluster back at the monolith. It is the same command with the other mode and
is printed by `route-flip.sh` before every flip:

| Flip | Rollback |
| --- | --- |
| identity → service | `route-flip.sh identity monolith` (check jwks `kid` match first, section 3.3) |
| flight → service | `route-flip.sh flight monolith` |
| passenger → service | `route-flip.sh passenger monolith` |
| booking → service | `route-flip.sh booking monolith` |

Kubernetes: add `--target k8s` (or `kubectl rollout undo deploy/gateway`). Aspire: set
`Gateway__Clusters__<ctx>` back to the API endpoint (or `--AppHost:Topology=Monolith`).

After a rollback:

1. If the context shares the monolith's store, set `Modules__<Module>__BackgroundProcessingEnabled=true`
   on the monolith and turn background processing off in the service (scale it to zero or set
   the same switch) so there is still exactly one processor.
2. If the context had already separated its data (Phase B) and took writes on the service,
   reverse-copy those writes before or right after the flip back (section 4), otherwise they are
   invisible to the monolith.
3. Confirm with `route-flip.sh status` and the monitoring signals above; file an incident with the
   gateway log lines (`Proxying to`), trace ids (`X-Trace-Id` response header) and correlation ids.

### Rollback validation (local, docker compose)

`validate-rollback.sh` rehearses a full flip and rollback for one route and fails unless traffic
demonstrably moved every time (`X-Gateway-Upstream` header, the gateway's `Proxying to` log line,
and request log lines appearing only in the expected container):

```bash
export SMOKE_USERNAME=samh SMOKE_PASSWORD='Admin@123456' SMOKE_CLIENT_ID=client SMOKE_CLIENT_SECRET=secret  # local dev seed
docker compose -f deployments/docker-compose/docker-compose.services.yaml \
               -f deployments/docker-compose/docker-compose.phase-a.yaml \
               -f deployments/docker-compose/docker-compose.cutover.yaml up -d --build --wait gateway
deployments/scripts/validate-rollback.sh flight     # monolith -> service -> monolith
deployments/scripts/validate-rollback.sh identity   # same, plus: both hosts publish the same JWKS kids
```

`docker-compose.cutover.yaml` adds the monolith to the multi-service topology (same `infrastructure`
network, so the two compose projects never both start the fixed-name infrastructure containers),
makes every gateway cluster switchable through `GATEWAY_<CTX>_UPSTREAM` (written to the git-ignored
`.env` by `route-flip.sh`), mounts the shared Identity key into the monolith and enables
`X-Gateway-Upstream` for authenticated callers. `docker-compose.phase-a.yaml` holds the Phase A data
and key configuration (flight-service on the monolith's Flight store, identity-service on the shared
signing key) and is kept after the monolith is gone, until Phase B retires each override. If host ports are taken on your machine, add a local
override with `EXTRA_COMPOSE_FILES=/path/override.yaml` (all scripts honour it). Output from the
AB-248 run is in the PR description.

## 8. Monolith decommission checklist

Do **not** start until all four contexts have been on their services for the burn-in period,
`route-flip.sh status` shows `service` for every context, and the `monolith` fallback cluster has
served no traffic for 7 days (gateway `Proxying to http://booking_modular_monolith` = 0).

**Step 1 — remove the running host** (`deployments/scripts/decommission-monolith.sh`). The script
fails closed on missing evidence: every cluster must report `service`, the gateway container/pod
must have been running for the whole quiet period (`--quiet-hours`, default 168) so its log can
prove silence — after a recent gateway recreate use the aggregated log store instead
(Loki: `{container="gateway"} |= "Proxying to http://booking_modular_monolith"` over the period) —
and the log must be readable and contain zero `Proxying to http://booking_modular_monolith` lines
(`--check-only` to just report). It then stops and removes the monolith host and, on compose,
**recreates the gateway from `docker-compose.services.yaml` + `docker-compose.phase-a.yaml`**,
dropping the cut-over overlay (no monolith dependency, clusters fixed to the services) but keeping
the Phase A file, so flight-service stays on the shared Flight store and identity-service on the
shared signing key — a redeploy without the Phase A file would silently move Flight back to the empty
`flight_service` databases and give identity-service a fresh key; `--target k8s` scales the monolith
deployment to 0. Finally it proves the gateway serves all four contexts with the monolith gone: one
authenticated request per context must succeed and the gateway log must show it proxied to the
service and nothing proxied to the monolith. From then on deploy with `docker-compose.services.yaml` +
`docker-compose.phase-a.yaml` — the cut-over overlay would start the monolith again. Remove a
context's override from the Phase A file as part of its Phase B (Flight: after the copy and repoint;
Identity: when it moves to the production key store). The host can still be started by hand until
Step 2 lands; keep the image and database backups for the retention period.

```bash
deployments/scripts/decommission-monolith.sh --check-only
deployments/scripts/decommission-monolith.sh              # or: --target k8s --namespace booking
```

**Step 2 — remove the code.** Each item is a separate, reviewable PR.

**Gateway**
- [ ] Remove the `monolith` cluster and the `api-fallback` / `monolith-fallback` routes from `src/Gateway/src/appsettings.json` and `appsettings.docker.json`; unknown paths then 404 at the gateway. Update `GatewayRouteConfigTests` (`every_cluster_initially_targets_the_monolith`) and `GatewayIngressTests`.
- [ ] Set each cluster's default destination to its service and rename the destination key from `monolith` to `primary` (update `route-flip.sh`, the compose files, Aspire and README together).
- [ ] Delete `deployments/docker-compose/docker-compose.cutover.yaml`, `deployments/scripts/validate-rollback.sh` and `deployments/scripts/decommission-monolith.sh`; keep `route-flip.sh` only if a rollback target still exists. Delete `docker-compose.phase-a.yaml` once every override in it has been retired by Phase B (Flight on its own store; identity-service on a production key store).

**Monolith host and modules**
- [ ] Delete `src/Api` (host, `Dockerfile`, `appsettings*.json`, `tempkey.jwk`, `Extensions/SharedInfrastructureExtensions.cs` module composition) and remove `Api` from `booking-modular-monolith.sln`.
- [ ] Remove the monolith-only composition code from modules (`*ModuleExtensions`/module registration used only by `src/Api`), the `Modules:<Module>:BackgroundProcessingEnabled` switch and `ModuleBackgroundProcessing` in `BuildingBlocks.Core` once no host composes more than one module (ADR 0013 follow-up), and their tests.
- [ ] Move module tests that boot `src/Api` (integration/end-to-end tests using `WebApplicationFactory<Api.Program>`) to the service hosts or delete duplicates.

**Data**
- [ ] Complete Phase B (section 4) for every context; rename `*_modular_monolith` databases or keep them as the service database and update `01-service-databases.sql` (drop `flight_modular_monolith` after Flight's copy is verified and retained read-only for the retention period).
- [ ] Remove the monolith's RabbitMQ queues/exchange bindings (`ServiceName: booking` consumers registered by `src/Api`) after confirming they are empty.

**Compose / Aspire / CI**
- [ ] `deployments/docker-compose/docker-compose.yaml`: remove `booking_modular_monolith` and the monolith-pointing gateway; make the multi-service topology the default (or delete this file in favour of `docker-compose.services.yaml`).
- [ ] Aspire: remove the `api` resource and `AppHost:Topology=Monolith` from `src/Aspire/src/AppHost` (ADR 0015) and its tests.
- [ ] CI: remove `src/Api` build/test/publish steps from `.github/workflows/ci.yml` and the monolith image from `.github/workflows/docker-images.yml` / `.github/actions/docker-build-publish`.
- [ ] Remove `src/Gateway/src/appsettings.docker.json` destinations that point at `booking_modular_monolith`, and every `booking_modular_monolith` reference in service `appsettings.docker.json` (`Jwt:MetadataAddress`).

**Docs**
- [ ] README: drop the monolith run instructions and the "Monolith API" column; `docs/target-architecture.md`: remove the transition-state section; mark ADR 0013 superseded and ADR 0016 implemented.
- [ ] Close AB-248 / AB-231.
