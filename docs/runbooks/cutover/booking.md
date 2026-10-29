# Step 3 — Booking

The host is now retired. These handover/rollback steps require the
[previous-release recovery overlay](decommission.md) and exported `MONOLITH_IMAGE`
before targeting monolith; normal startup is service-only.

## Pre-checks and data separation

Flight, Identity and Passenger must be stable. Apply [common gates](README.md).
Booking has no authoritative booking_modular_monolith PostgreSQL database: preserve the existing
EventStoreDB events, stream IDs and ordering; configure EventStoreOptions__ConnectionString.
Move Booking records from shared persist_message to booking_service_persist_message owned by
booking_service, retaining inbox deduplication and draining old outbox before owner transfer.

The catch-up subscription uses $all, default SubscriptionId=default and checkpoint_default in
EventStoreDB (BuildingBlocks/EventStoreDB/Subscriptions). Record its committed position before
stopping monolith. Start only ONE active subscription, never service and monolith together.
Modules__Booking__BackgroundProcessingEnabled controls it; Compose leaves service=true and
monolith=false. Do not reset a checkpoint on the live projection to force replay.
For booking_service_read rebuild into an isolated Mongo database using a dedicated offline replay
and independent checkpoint, then compare counts/IDs and switch MongoOptions__Booking__DatabaseName.
This is an operator backfill plan, not an existing replay command. Keep live writer/subscriber single-active.
Set PersistMessageOptions__ConnectionString__Booking to the new message store and retain all
EventStore stream/checkpoint data in backups. Confirm Booking's gRPC clients resolve Flight and
Passenger at port 8081; gateway HTTP routing cannot redirect these calls.

## Flip / verification

```bash
scripts/cutover/flip-route.sh booking service
scripts/cutover/validate-rollback.sh booking
```

The latter is a local routing drill with a negative read probe, not a business-flow substitute.
Through :5000 register a test user, wait for Passenger, select an available flight and seat, then
POST /api/v1/booking with passengerId, flightId and description. Require 200 and booking ID,
one persisted event sequence, one reserved seat and the Mongo projection caught up.
Confirm YARP's upstream is booking-service and checkpoint progresses without competing writers.

## Rollback

```bash
scripts/cutover/flip-route.sh booking monolith
```

Trigger on gRPC failures, duplicate/missing events, seat invariant failures, stalled checkpoint,
wrong upstream or agreed error/latency/lag breach. Before changing background ownership, stop the
service subscription, record checkpoint, and only then enable monolith's subscription.
For a short HTTP-only drill keep service as subscriber and monolith background processing off.
Both hosts must share the current EventStore, persist-message and Mongo state; otherwise rollback
requires reconciliation under a write pause, not just config.
Canary/dark launch is read-only against isolated projections (common README); never shadow booking
or seat-reservation commands. Progress to decommission only after the agreed N-day soak.
