# ADR 0001: Service boundaries — one service per existing module

## Status

Accepted

## Context

The application is a .NET modular monolith with four modules under `src/Modules`,
each already a bounded context with its own database, its own EF Core migrations,
and communication only through gRPC or MassTransit integration events:

- `Identity` — authentication/authorization (Duende IdentityServer, Postgres)
- `Flight` — flight scheduling and seat inventory (CRUD, Postgres write / MongoDB read)
- `Passenger` — passenger profiles (Postgres write / MongoDB read)
- `Booking` — reservations (event-sourced on EventStoreDB, MongoDB read)

We need to decide how to cut services when extracting them from the monolith.

## Decision

Extract **one microservice per existing module**: `identity-service`, `flight-service`,
`passenger-service`, and `booking-service`. Module boundaries are the service boundaries;
no module is split or merged during the migration.

Rationale:

- The modules already map 1:1 to bounded contexts in the domain.
- Cross-module coupling is already limited to explicit contracts (gRPC protos and
  integration events), so the extraction seams already exist.
- Each module already owns a distinct data store, so no data untangling is required.

## Consequences

- Extraction is mechanical: lift a module plus its slice of `BuildingBlocks` into its own
  deployable host (see AB-233 for splitting `BuildingBlocks`).
- Re-drawing boundaries (e.g. splitting Booking into booking + payments) is deferred and
  would be a new ADR.
- Four services is the target end state; the monolith remains a fifth deployable during
  the transition (ADR 0002).
