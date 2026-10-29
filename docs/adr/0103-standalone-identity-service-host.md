# ADR 0103: Standalone Identity service host

- Status: Proposed (ARB review required; ticket to be created)
- Ticket: AB-493 (epic AB-488, Microservices Migration Scaffolding)
- Date: 2026-10-29

## Context

The Identity module currently runs inside the monolith API, alongside Flight, Passenger, and Booking. The migration needs Identity to run as an independent process while preserving the existing module implementation and contracts. The module already owns its EF Core context and outbox processor, and its integration events are transported through RabbitMQ.

Running both hosts at once must not cause the monolith and standalone host to poll the same Identity outbox. The standalone service also needs a dedicated database so its identity data and outbox can be managed independently from the monolith.

## Decision

1. Add `Identity.Host` as an ASP.NET Core host that composes the existing Identity module and shared ServiceDefaults. It references only Identity and focused shared BuildingBlocks; it does not reference the monolith API or other business modules.
2. Configure the host to use the `identity_service` PostgreSQL database for Identity data and the module-owned outbox. Configure RabbitMQ and health/telemetry endpoints through host configuration.
3. Register MassTransit with only the Identity module assembly. Do not re-register the Identity persistence processor at the host layer; the module already owns that registration.
4. Keep the monolith's Identity endpoints available for the transition, but disable its Identity background processor when both hosts run side by side. Route gateway Identity traffic to the standalone host.
5. Keep service-host composition explicit for now. A shared ServiceHost library was considered but rejected by the AB-488 epic decision; the small host-specific composition is clearer than introducing a common abstraction before additional service hosts establish stable shared needs.

## Consequences

- Identity can be deployed and scaled independently, with a service-owned database and outbox.
- Side-by-side operation requires the monolith Identity background processor to be disabled and gateway routing/issuer metadata to be configured consistently.
- Local developer signing keys differ between the two processes; tokens are not interchangeable unless an environment provisions a shared signing key.
- The outbox worker has no distributed lease, so operate a single Identity host replica until coordination is added.
- Existing Identity data must be migrated from `identity_modular_monolith` to `identity_service` as part of AB-505 before production cutover.
- The host has its own container build, integration tests, and service runbook.

## Alternatives considered

- **Keep Identity only in the monolith:** rejected because it does not enable an independent Identity service or the migration's service-boundary validation.
- **Introduce a shared ServiceHost library:** rejected by the AB-488 epic decision; defer until the service hosts demonstrate stable common composition needs.
- **Move Identity module code into the host:** rejected because it would duplicate or relocate module ownership rather than reuse the existing module.
