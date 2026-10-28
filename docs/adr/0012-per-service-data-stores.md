# 0012: Per-service data stores

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

## Configuration and implementation

- `PostgresOptions:ConnectionString:<Module>` configures write models.
- `PersistMessageOptions:ConnectionString:<Module>` configures module outbox/inbox
  storage (fallback: `ConnectionStrings:<module>-persist-message`).
- `PersistMessageOptions:Interval` remains the common polling interval.
- `Modules:<Module>:BackgroundProcessingEnabled` defaults to true and gates only
  that module's polling worker.
- Named `MongoOptions:<Module>:ConnectionString` falls back to root
  `MongoOptions:ConnectionString`; `MongoOptions:<Module>:DatabaseName` is required.

Typed persistence contexts, processors, publishers and dispatchers prevent
last-registration-wins store selection. Generic persistence contexts inherit the
common model and table shape. Module migration startup seeds only seeders from
the migrated context's assembly. Booking persistence is registered in the API
because the Booking module does not reference persistence infrastructure.

Message processing semantics and MassTransit consumer wiring are owned by AB-501;
this decision isolates storage without changing delivery or deduplication behavior.
Mongo databases have separate names; per-module Mongo authentication is deferred.

## Migration

Existing data is not copied automatically. Data migration and production cutover
are deferred to AB-505. Do not run this against existing production volumes as a
data migration procedure.
