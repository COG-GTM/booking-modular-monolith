# 0505. Cut-over, rollback and decommission of the monolith host

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Authors:** Devin (AB-505)
- **Owning team:** TBD — owner to confirm before ARB
- **Related ADRs:** 0001 (gateway single ingress), 0002 (strangler fig), 0004 (database per
  service), 0005 (side-by-side background-processing switch), 0492, 0493, 0496, 0498, 0502

## Context

All four service hosts, the YARP gateway (one cluster per context, destination `monolith`), RabbitMQ
and the service-owned databases exist. Traffic still goes to `src/Api`, whose `*_modular_monolith`
databases hold all production data. ADR 0002 deferred the data migration and the decommission of the
host to this ticket and sketched a Flight-first order.

ARB triggers: T4/T5 (data migration between the monolith's and the services' databases, removal of
a deployable - the monolith host - and of its CI image), T6 (gateway routing changes: every cluster
now targets a service). Detector false positives: T1 (no new service - the cut-over compose overlay
only re-adds the existing monolith next to existing hosts for the transition), T3 (no new external
dependency; `pg_dump`/`mongodump` ship in the already-used postgres/mongo images).

## Decision

1. **Order Identity -> Flight -> Passenger -> Booking**, one context per window with burn-in.
   Identity first because every other context validates its tokens: moving it while nothing else has
   moved isolates token-continuity problems. Booking last because it calls Flight and Passenger over
   gRPC. (Supersedes the Flight-first sketch of ADR 0002.)
2. **A flip is an atomic hand-over of route + data + background ownership** (`deployments/cutover/
   flip-route.sh`): freeze the context's gateway route, drain the owner's outbox, switch off its
   `Modules:<M>:BackgroundProcessingEnabled`, copy Postgres + Mongo data with row/document-count
   verification, switch on the new owner, repoint
   `ReverseProxy__Clusters__<m>__Destinations__monolith__Address`, verify the upstream from the
   gateway's proxy log. A short per-context write outage (502 for that context, ~1-2 min) is accepted
   in exchange for no dual writes and no split-brain read models.
3. **Rollback is the same procedure in reverse**, including copying the service's data back, so it is
   lossless at any point before decommission (validated for Flight on compose).
4. **Booking's EventStoreDB is shared, not migrated**: both hosts use the same streams and the same
   checkpoint stream; only the Mongo read model moves.
5. **Identity signing material is shared** between the two Identity hosts (developer key + Data
   Protection key ring for Duende's automatic keys) so tokens survive an Identity flip either way.
6. **Canary only reads, or only while stores are shared** (header route or extra destination);
   default is dark launch with background processing off.
7. **Decommission** removes `src/Api` (project, Dockerfile, monolith compose, CI image, Aspire
   resource); module test projects run against the service hosts; gateway defaults point every
   cluster at its service. Monolith databases are kept read-only for a retention period, then dropped.

## Alternatives considered

- **Online dual-write / CDC sync instead of a freeze**: zero downtime but needs a replication pipeline
  and conflict handling for both directions; disproportionate for current data volumes. Documented as
  the scale-up path (logical replication pre-seed, copy becomes verify-only).
- **Services keep using the monolith databases (shared DB) after cut-over**: makes flips config-only
  but violates ADR 0004 and leaves decommission blocked on a later, riskier data split.
- **Weighted canary with writes on both sides**: splits data between two owners; rejected.

## Consequences

- Per-flip write outage for one context; runbook defines monitoring thresholds and rollback triggers.
- After decommission there is no monolith fallback; rollback means redeploying previous service images.
- Production execution needs secrets-managed DB credentials and the same steps in the deployment
  platform (env vars on gateway/host deployments); the compose scripts are the reference implementation.
