# Strangler cutover and rollback

This runbook moves module ownership between the legacy monolith and its service host. The
cutover overlay is for local drills; it is not a production deployment manifest. Keep the
stack running when collecting evidence.

## Prerequisites

- Docker Engine and Compose, `jq`, and `curl`.
- The service topology and its infrastructure are defined in `docker-compose.yaml`.
- Build the legacy image before removing `src/Api`:

  ```bash
  scripts/cutover/build-legacy-monolith.sh
  ```

- If host port 8080 is occupied, set `CADVISOR_HOST_PORT` to an unused port, for example
  `18080`.

## Start and inspect the drill stack

```bash
CADVISOR_HOST_PORT=18080 scripts/cutover/cutover.sh up
scripts/cutover/cutover.sh status
```

The gateway is the public ingress at `http://localhost:5000`. The monolith diagnostic
endpoint is loopback-only at `http://127.0.0.1:3001`. Its image is selected by
`MONOLITH_IMAGE` (default `booking-modular-monolith:legacy`).

The script creates `deployments/docker-compose/.cutover/state.env` and generates
`deployments/docker-compose/.cutover/gateway/overrides.json`. Do not edit either file
while a flip is in progress. The state file tracks each module's owner and background
worker switches; only the active owner runs background processing.

## Validate rollback and Identity continuity

Run the Flight rollback drill while `flight-service` owns the route. It writes through the
gateway, copies service data back to the legacy database, and proves the monolith can read
the write after the route returns:

```bash
scripts/cutover/validate-rollback.sh flight
```

Mint a token before moving Identity, then verify it remains accepted after the Identity
route changes:

```bash
scripts/cutover/validate-token-continuity.sh
```

Identity signing keys and Data Protection keys are mounted from shared named volumes in
the service topology and cutover overlay. The drill should be considered incomplete
unless both hosts use those mounts and tokens minted before a flip validate afterward.

## Move ownership

Move one context to a service or back to the monolith:

```bash
scripts/cutover/cutover.sh flip flight service
scripts/cutover/cutover.sh flip flight monolith
```

The script freezes the context-specific route set, quiesces the old worker, stops the
target host, copies and verifies data, starts the new owner, then unfreezes and probes
the route. Freeze sets are Flight `{flight, booking}`, Passenger `{passenger, booking}`,
Booking `{booking}`, and Identity `{identity}`. If a flip fails, leave the affected route
frozen and inspect the state and Compose logs before continuing.

To move all modules to service hosts and retire the monolith:

```bash
scripts/cutover/cutover.sh all
scripts/cutover/cutover.sh decommission
scripts/smoke/compose-e2e.sh
```

PostgreSQL table counts are checked after restore. Flight and Passenger Mongo read models
are copied and checked by collection counts; Booking's read model is rebuilt from the
shared EventStoreDB rather than copied. Identity copies PostgreSQL data only.
Flight synchronization reconciles Mongo seat IDs to the copied PostgreSQL seat IDs by
flight and seat number. Existing local stores can contain mismatched projection IDs,
which otherwise prevents a successful seat reservation from updating its read model.

## Roll back

Flip each module back explicitly; the reverse copy happens before the gateway route moves:

```bash
scripts/cutover/cutover.sh flip flight monolith
scripts/cutover/cutover.sh flip passenger monolith
scripts/cutover/cutover.sh flip booking monolith
scripts/cutover/cutover.sh flip identity monolith
```

An interrupted flip records its stage in the state file. Resume the same flip command.
Aborting restores routing to the currently authoritative owner without copying data.
Keep the legacy image available after decommission so rollback remains possible.
