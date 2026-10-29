# Monolith decommission (later phase — not performed by AB-505 phase 1)

## Go/no-go

All four clusters must have routed to services for agreed N consecutive days. N is signed off by
Booking team/on-call and ARB before rollout. Prove monolith cluster/fallback has zero traffic for
the same interval, including unmatched paths, discovery and token issuance. Use route-level gateway
logs/metrics; checking only four destination settings is insufficient.
Services own data, inbox/outbox processing and the sole Booking subscription; no legacy consumers
or scheduled tasks remain. Verify backup restore, dashboards, alerting, service health, signing-key
persistence and registration-to-booking sanity. Archive route state and prior release image/tag.

## Steps (separate approved change)

1. Disable/stop the optional monolith deployment; observe for an agreed window and verify no 502
   fallback traffic or service workflow failures. No stores/volumes are removed.
2. Replace/remove the gateway fallback-to-monolith route and monolith cluster in the later change;
   choose an explicit unknown-path 404 policy and test it. Stop if any legitimate traffic used it.
3. Remove src/Api and its build/deployment/test references only after dependency inventory and
   passing service/gateway tests. Update docs and release artifacts; retain previous release images.
4. Clean up monolith deployment/profile configuration, obsolete secret access and consumer queues
   after checking no unprocessed messages. Retain data and backups for the approved retention period.
5. Run service health, JWT/discovery, UserCreated delivery, Flight/Passenger gRPC and CreateBooking
   smoke checks; monitor queue lag, projections, subscription checkpoint and error/latency budgets.

## Rollback after decommission

There is no route-only rollback once the monolith artifact/configuration is retired.
Redeploy the **previous release tag** (recorded at go/no-go) containing src/Api and the gateway
monolith route/cluster, using current service stores, compatible schemas and signing keys.
Quiesce writes if data/schema reconciliation is required. Transfer background/consumer ownership
with only one owner active, verify monolith readiness, then route affected clusters back (Identity
and Passenger together). Run the same authenticated probes and business sanity.
Never restore an old database over newer accepted service writes. Incident commander stops the
change if readiness, token validation, data invariants or agreed budgets fail.
