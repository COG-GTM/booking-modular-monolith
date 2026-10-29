# Monolith decommission and previous-release recovery

AB-505 phase 2 removes the host, its build references, default Compose deployment and gateway
fallback. Unknown paths now return 404. The production gates below still require approval;
repository retirement is not evidence that production has completed its data handover or soak.
Aspire AppHost is also Services-only: no monolith mode, project, cluster or fallback remains.
Use the previous-release Compose overlay below for recovery, not an AppHost topology flag.

## Go/no-go

All four clusters must have routed to services for agreed N consecutive days. N is signed off by
Booking team/on-call and ARB before rollout. Prove monolith cluster/fallback has zero traffic for
the same interval, including unmatched paths, discovery and token issuance. Use route-level gateway
logs/metrics; checking only four destination settings is insufficient.
Services own data, inbox/outbox processing and the sole Booking subscription; no legacy consumers
or scheduled tasks remain. Verify backup restore, dashboards, alerting, service health, signing-key
persistence and registration-to-booking sanity. Archive route state and prior release image/tag.

## Production execution gates

1. Disable/stop the optional monolith deployment; observe for an agreed window and verify no 502
   fallback traffic or service workflow failures. No stores/volumes are removed.
2. Deploy service-only gateway routing (no fallback/monolith cluster); verify unknown-path 404.
   Stop if any legitimate traffic relied on the removed fallback.
3. Deploy service hosts and retargeted tests/build artifacts. Retain previous release images.
4. Clean up monolith deployment/profile configuration, obsolete secret access and consumer queues
   after checking no unprocessed messages. Retain data and backups for the approved retention period.
5. Run service health, JWT/discovery, UserCreated delivery, Flight/Passenger gRPC and CreateBooking
   smoke checks; monitor queue lag, projections, subscription checkpoint and error/latency budgets.

## Rollback after decommission

There is no route-only rollback once the monolith artifact/configuration is retired.
Redeploy the **previous release tag/image** (recorded at go/no-go), using current service stores,
compatible schemas and signing keys. Do not build the retired host from this checkout.
For local recovery, the explicit overlay restores the previous image and gateway cluster:

```bash
export MONOLITH_IMAGE=<previous-release-image:tag>
touch deployments/docker-compose/.cutover.env
docker compose -f deployments/docker-compose/docker-compose.yaml \
  -f deployments/docker-compose/docker-compose.monolith-rollback.yaml \
  --env-file deployments/docker-compose/.cutover.env up -d booking_modular_monolith
scripts/cutover/flip-route.sh flight monolith
# After recovery:
scripts/cutover/flip-route.sh flight service
docker compose -f deployments/docker-compose/docker-compose.yaml \
  -f deployments/docker-compose/docker-compose.monolith-rollback.yaml rm -sf booking_modular_monolith
```

The overlay does not restore a catch-all route. For the automated service → previous image →
service proof, run `MONOLITH_IMAGE=<previous-release-image:tag> scripts/cutover/validate-rollback.sh flight`.
Restore every affected cluster to services before stopping the previous image. The overlay's
background flags disable polling/subscriptions but not legacy MassTransit consumers.
Quiesce writes if data/schema reconciliation is required. Transfer background/consumer ownership
with only one owner active, verify monolith readiness, then route affected clusters back (Identity
and Passenger together). Run the same authenticated probes and business sanity.
Never restore an old database over newer accepted service writes. Incident commander stops the
change if readiness, token validation, data invariants or agreed budgets fail.
