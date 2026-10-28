# 0012: Per-service data stores

- **Status:** Proposed
- **Date:** 2026-10-28
- **ARB ticket:** TO BE CREATED
- **Authors:** AB-492 (epic AB-488)
- **Owning team:** TBD — owner to confirm before ARB
- **Related ADRs:** [0004](0004-database-per-service-and-per-service-outbox-inbox.md), [0021](0021-rabbitmq-delivery-and-messageid-inbox.md)

## Context

A shared persist-message store and unqualified context registrations couple module
delivery and data ownership. Shared PostgreSQL credentials allow cross-module
database access. These boundaries must be isolated before service extraction.
This changes database ownership and store topology, requiring ARB review (T2/T8).

## Decision

Each module owns its PostgreSQL database, persistence-message database, and (where
used) MongoDB read database. Flight, Identity and Passenger use
`<module>_service` for write models. All four modules use
`<module>_service_persist_message`; Flight, Passenger and Booking use
`<module>_service_read` in MongoDB. Booking retains EventStoreDB.

The local PostgreSQL initialization script creates one login per module, owning
only its databases. Public CONNECT is revoked on each service database. Local
passwords equal role names; deployments must provide managed credentials.
The script runs only for a fresh PostgreSQL volume.

## Alternatives considered

- Keep shared stores: rejected because module extraction would retain shared data ownership.
- Separate database servers immediately: deferred; logical database and role isolation
  establishes ownership without requiring additional infrastructure now.

## Architecture

- `PostgresOptions:ConnectionString:<Module>` configures write models.
- `PersistMessageOptions:ConnectionString:<Module>` configures module outbox/inbox
  storage (fallback: `ConnectionStrings:<module>-persist-message`).
- `PersistMessageOptions:Interval` remains the common polling interval.
- `Modules:<Module>:BackgroundProcessingEnabled` defaults to true and gates only
  that module's polling worker.
- Named `MongoOptions:<Module>:ConnectionString` falls back to Aspire's
  `ConnectionStrings:mongo`, then root `MongoOptions:ConnectionString`;
  `MongoOptions:<Module>:DatabaseName` is required.

Typed persistence contexts, processors, publishers and dispatchers prevent
last-registration-wins store selection. Generic persistence contexts inherit the
common model and table shape. Module migration startup seeds only seeders from
the migrated context's assembly. Booking persistence is registered in the API
because the Booking module does not reference persistence infrastructure.

Message processing semantics and MassTransit consumer wiring are owned by AB-501;
this decision isolates storage without changing delivery or deduplication behavior.
Mongo databases have separate names; per-module Mongo authentication is deferred.

## Non-functional requirements

Each module polls only its own store, independently of other module workers.
Delivery and deduplication semantics remain those of AB-501. Throughput, recovery
objectives and SLOs are TBD — owner to confirm before ARB.

## Security & compliance

PostgreSQL roles own only their module's databases, with PUBLIC CONNECT revoked.
Local development credentials must be replaced with managed deployment credentials.
Mongo database names isolate ownership, not authorization; module-specific Mongo
users and production credential rotation remain deployment follow-ups.

## Cost

Logical databases share the existing PostgreSQL and Mongo servers. Additional
connections and workers consume resources; production cost and capacity estimates
are TBD — owner to confirm before ARB.

## Operations

Compose initialization runs only on fresh PostgreSQL volumes. Health checks cover
each configured write and persist-message database. Operators must monitor module
worker failures and disable individual workers through their background-processing
flag when required. Backups and restores must include the owning module's stores.

Existing data is not copied automatically. Data migration and production cutover
are deferred to AB-505. Do not run this against existing production volumes as a
data migration procedure.

## Policy exceptions requested

N/A — no policy exception is requested. ARB approval remains required before merge.

## Consequences

Module-specific registrations prevent last-registration-wins data routing and
PostgreSQL roles enforce database access boundaries. Configuration, connection
counts, backups and migration coordination now scale with the number of modules.
Shared server failures can still affect all modules.

## Open questions

- Owning team, production capacity/cost estimates, SLOs and recovery objectives:
  TBD — owner to confirm before ARB.
- ARB ticket must be created and approved; status remains Proposed until approval.
- AB-505 must define production migration, cutover and rollback procedures.
