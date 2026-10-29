# Cutover and rollback (AB-505)

The monolith host is decommissioned. Default gateway routing is service-only, with unknown paths
returning 404. The migration sequence below remains the production handover checklist; recovery
requires a prebuilt previous-release image via the [rollback overlay](decommission.md).
Follow ADR [0007](../../adr/0007-extraction-order.md):
[Flight](flight.md) → [Identity + Passenger](identity-passenger.md) → [Booking](booking.md).
See the [before/during/after diagram](../../architecture/cutover.md) and
[decommission procedure](decommission.md). Production execution requires ARB approval;
ADR [0027](../../adr/0027-cutover-and-monolith-decommission.md) is Proposed.

## Preconditions / go-no-go

Booking team incident commander owns the change window; nominate a separate rollback operator.
Record release tags, backup locations, route state, store connection strings (without passwords),
queue depths, projection lag and EventStore checkpoint. Restore backups in an isolated environment first.
Agree N (soak days), error/latency budgets, queue-lag limits and RPO/RTO before starting:
these are owner decisions, not implied defaults. No-go if unknown or exceeded.

Require healthy gateway/services/monolith, compatible contracts/schema, reconciled data counts and
sample IDs, valid issuer/JWKS and existing tokens, gRPC reachability and broker connectivity.
Quiesce writes during data transfer; disable the old owner before enabling the new background owner.
Do not permit competing outbox workers or Booking subscriptions.

**Compose caveat:** module BackgroundProcessingEnabled=false disables polling/subscription, NOT
MassTransit consumer registration. The previous-release monolith still consumes UserCreated. Keep it only
for short rollback drills. Production coexistence requires isolating/disabling the legacy consumer
before allowing registration writes. Do not infer safe production dual-running from a local drill.

## Route state

| Stage | Flight | Identity | Passenger | Booking | Monolith |
| --- | --- | --- | --- | --- | --- |
| Before | monolith | monolith | monolith | monolith | required |
| 1 | service | monolith | monolith | monolith | required |
| 2 | service | service | service | monolith | required |
| 3 / soak | service | service | service | service | retained, no traffic |
| After N days | service | service | service | service | retired |

This table is the production migration sequence; plain Compose already defaults to all services.
The fallback catch-all and monolith cluster are removed from default routing.

## Local tooling

From repository root (Docker Compose v2, bash, curl, jq, grep, flock required):

```bash
export MONOLITH_IMAGE=<previous-release-image:tag>
scripts/cutover/validate-rollback.sh flight 2>&1 | tee ~/ab505-rollback-flight.log
scripts/cutover/validate-rollback.sh passenger
```

Use `set -o pipefail` when capturing output so tee cannot mask failure.
The validator starts only application services and their required infrastructure, not observability.
It checks HTTP results and YARP's actual proxy destination with a fresh GUID in both correlationId
and cutoverProof query parameter per request. Identity/Booking negative read probes expect 404
(they have no list/read endpoint); these prove routing/authenticated ingress, not functional success.
Flight must return 200. Full registration/booking business sanity is separately required.

`flip-route.sh <cluster> <monolith|service>` changes only gateway destination overrides and runs
`docker compose up -d --no-deps gateway`. Identity and Passenger are changed atomically in one
gateway recreation. Monolith readiness and MONOLITH_IMAGE are mandatory before a reverse flip.
Start the previous-release image using the overlay first; the validator does this automatically.
Service flips require target-container readiness before changing persisted state. Every flip then
checks an authenticated routed request and GUID-linked upstream logs, not just gateway self health.
On a failed drill, an EXIT trap restores the initial paired destinations through the locking flip
script; restoration errors print an explicit manual rollback command. Custom initial destinations
are rejected before the drill because the flip script supports only service/monolith targets.
Token credentials and bearer headers are passed to curl over stdin, not in process arguments.
State is in git-ignored `deployments/docker-compose/.cutover.env`, not application source.
Do not run concurrent compose operations during a flip. The persistent lock file is also ignored.
A failed recreation leaves desired state on disk: inspect containers and explicitly retry/roll back.
The script does not transfer data or background ownership.
The monolith does not expose /health/ready in its docker environment: the flip gate checks its
OIDC discovery endpoint returns 200. This is application responsiveness, not a complete database
health check; the pre-checks and authenticated business probes remain mandatory.

For subsequent manual Compose operations preserve state explicitly:

```bash
docker compose -f deployments/docker-compose/docker-compose.yaml \
  -f deployments/docker-compose/docker-compose.monolith-rollback.yaml \
  --env-file deployments/docker-compose/.cutover.env up -d
docker compose -f deployments/docker-compose/docker-compose.yaml \
  -f deployments/docker-compose/docker-compose.monolith-rollback.yaml down
```

Without --env-file the default is all services; never accidentally reset routes mid-incident.
Do not remove volumes during rollback. Keep built images for the next drill.

## Data handover (mandatory for each step)

Production legacy databases are flight_modular_monolith, identity_modular_monolith,
passenger_modular_monolith and shared persist_message. Current repository appsettings and Compose
already target service databases: inventory the actual deployed legacy settings before copying.
Booking's authoritative data is EventStoreDB, not a booking relational database.

For each module, stop ingress writes, drain its old outbox and consumer inbox, record remaining
message IDs and broker queue depths, then stop the old background owner. Back up both relational
data and persist_message. Copy business rows with stable IDs, relationships and concurrency
versions; preserve relevant inbox deduplication/message IDs if replay remains possible.
Do not copy a shared outbox wholesale to every service (duplicate delivery).
Reconcile rows, IDs and invariants before resuming. Never enable two dispatchers for the same data.
Verify source/sink counts and broker acknowledgements after resuming.

Once a service database accepts writes, rollback is **not config-only** unless monolith points to
that same database/read model and compatible schema. Set
`PostgresOptions__ConnectionString__<Module>`,
`PersistMessageOptions__ConnectionString__<Module>`,
`MongoOptions__<Module>__ConnectionString` and
`MongoOptions__<Module>__DatabaseName` consistently on the legacy deployment.
The retained phase-1 image shares these stores via its embedded appsettings.docker.json; verify
the chosen previous-release image's configuration before a drill. Otherwise quiesce writes and reconcile/reverse-copy before rollback;
restoring a stale backup would lose accepted writes.

## Canary / dark launch

Prefer a higher-priority YARP header-matched read route to a separate canary cluster, leaving the
normal route unchanged. Restrict the canary header at trusted ingress. YARP's current config has
one primary destination per cluster: the flip script performs a full switch, NOT a weighted rollout.
Weighted routing/shadowing needs an explicitly configured/custom policy (not provided here).
Shadow only idempotent reads to a non-writing replica, redact PII, compare status/payload/latency,
and disable background processors on that replica. Never duplicate registration, seat reservation
or booking commands. Promote only after parity and agreed budgets; reverse the canary route first
on errors. Token issuance requires consistent issuer and signing keys across any canary.
