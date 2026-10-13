# 0004. Data ownership: database per service, per-service outbox/inbox

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0001](0001-service-boundaries.md), [0005](0005-side-by-side-background-processing-switch.md);
  implemented by AB-492, data migration in AB-505

## Context

The monolith already keeps one Postgres database per module (`*_modular_monolith`) but shares
`persist_message` (outbox/inbox for all modules), one Mongo read database
(`booking_modular_monolith_read`) and one EventStoreDB. During the strangler-fig transition the
monolith keeps running on those stores. If a service host pointed at the monolith's stores, two
outbox pollers would process the same table and both processes would write the same read models.

## Decision

- **Each service owns its data. No service reads or writes another service's database**; data from
  another service is obtained via gRPC or integration events only.
- Each service host gets **its own new databases**, separate from the monolith's:

  | Service | Write store | Outbox/inbox (Postgres) | Read store (Mongo) | Event store |
  | --- | --- | --- | --- | --- |
  | Identity | Postgres `identity_service` | `identity_service_persist_message` | - | - |
  | Flight | Postgres `flight_service` | `flight_service_persist_message` | `flight_service_read` | - |
  | Passenger | Postgres `passenger_service` | `passenger_service_persist_message` | `passenger_service_read` | - |
  | Booking | EventStoreDB (only Booking uses it) | `booking_service_persist_message` | `booking_service_read` | EventStoreDB |

- Database **servers** may be shared in dev/test (one Postgres, one Mongo); the isolation boundary is
  the logical database and its credentials. Production may split servers per service.
- Every service has its **own outbox and inbox**, so exactly one poller processes each outbox and
  inbox deduplication is per consumer service.
- The monolith's `*_modular_monolith`, `persist_message` and `booking_modular_monolith_read` stores
  are left untouched. **Data migration from monolith DBs to service DBs is deferred to cutover**
  (AB-505).
- Postgres must run with `max_prepared_transactions` > 0 (100 in compose) because Flight uses
  `TransactionScope`.

## Alternatives considered

| Alternative | Pros | Cons | Why rejected |
| --- | --- | --- | --- |
| Services reuse the monolith's DBs | No data migration | Two outbox pollers on one table; dual writers on read models; hidden coupling | Breaks correctness during side-by-side |
| Shared DB with schema-per-service | One server/DB | Easy to cross-query; shared migrations history | Weaker isolation than DB-per-service for little gain |
| Outbox row locking so monolith + service share an outbox | Fewer DBs | Complex, still couples schemas | Simpler to give each host its own stores plus [0005](0005-side-by-side-background-processing-switch.md) |

## Consequences

- No cross-service joins; queries spanning services go through APIs or local read models built
  from events.
- More databases to provision, back up and migrate; EF migrations run per service.
- Cutover needs an explicit, rehearsed data-copy step per module (AB-505).
