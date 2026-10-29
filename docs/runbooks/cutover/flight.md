# Step 1 — Flight

The host is now retired. These handover/rollback steps require the
[previous-release recovery overlay](decommission.md) and exported `MONOLITH_IMAGE`
before targeting monolith; normal startup is service-only.

## Pre-checks and data separation

Complete the [common gates](README.md). Snapshot flight_modular_monolith and Flight's records in
shared persist_message. Provision flight_service and flight_service_persist_message, owned by
flight_service (see deployments/docker-compose/init/postgres). Drain/disable the old dispatcher,
copy aircraft, airports, flights and seats with original IDs/versions, transfer inbox deduplication
state, and reconcile seat inventory. Configure PostgresOptions__ConnectionString__Flight and
PersistMessageOptions__ConnectionString__Flight on the active host.
Rebuild flight_service_read from authoritative Flight data using the existing projection/seed
paths only after verifying they cover every historical record; no production backfill CLI is shipped.
Prepare an audited one-off backfill if needed. Compare Mongo IDs/counts and available flight/seat
responses. MongoOptions__Flight__DatabaseName selects the read database.
Check Booking Grpc__FlightAddress points at the intended healthy host (Compose: flight-service:8081).
HTTP route flips do not change Booking's gRPC configuration.

## Flip / verification

```bash
scripts/cutover/flip-route.sh flight service
scripts/cutover/validate-rollback.sh flight
```

The validator is a local rehearsal, not a production change command: it intentionally performs
three flips. In production send authenticated GET /api/v1/flight/get-available-flights and
GET /api/v1/flight/get-available-seats/{id} with a known flight; require 200, payload parity,
unique correlation proof of flight-service upstream, and an end-to-end booking/seat reservation.
Watch latency, exceptions, projection lag and inventory consistency.

## Rollback

```bash
scripts/cutover/flip-route.sh flight monolith
```

Trigger on failed readiness, wrong destination, auth errors, inventory mismatch, or agreed
error/latency/lag budget breach. Check common shared-store rollback prerequisites BEFORE flipping.
Reverify the same reads through gateway and booking's gRPC path; a REST rollback alone does not
redirect gRPC. Transfer background ownership separately with old owner stopped first.
For canary use header-routed Flight reads and dark comparisons per README; never shadow seat writes.
