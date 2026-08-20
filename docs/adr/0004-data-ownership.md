# ADR 0004: Data ownership — database per service

## Status

Accepted

## Context

Each module already has dedicated storage:

- Identity: Postgres (`identity_modular_monolith`)
- Flight: Postgres (`flight_modular_monolith`) + MongoDB read models
- Passenger: Postgres (`passenger_modular_monolith`) + MongoDB read models
- Booking: EventStoreDB (write/event stream) + MongoDB read models

However, the databases run on shared server instances and the persistence/outbox
(`persist_message`) store is shared across modules.

## Decision

**Each service exclusively owns its own database(s). No service reads from or writes to
another service's database — ever.**

- A service's Postgres/EventStoreDB/MongoDB data is private implementation detail;
  the only way to access another service's data is its gRPC API or its published events.
- Each service gets its **own outbox/inbox tables inside its own database** instead of the
  shared `persist_message` store (see AB-236).
- Read models a service needs about another context are built by subscribing to that
  context's integration events (local projections), not by querying foreign databases.
- Reporting/analytics needs that span services are served by event-driven projections,
  not cross-database joins.

## Consequences

- Services can evolve schemas independently and choose fitting storage technology.
- No distributed transactions: cross-service consistency is eventual, coordinated through
  the outbox pattern and integration events.
- Some data is duplicated (projections), which is accepted as the cost of autonomy.
- Shared database servers may still be used operationally (one Postgres cluster), but
  logical databases/credentials are strictly per service.
