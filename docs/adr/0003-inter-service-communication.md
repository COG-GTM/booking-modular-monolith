# ADR 0003: Inter-service communication — gRPC for sync, RabbitMQ for async

## Status

Accepted

## Context

Inside the monolith, modules already communicate through two mechanisms:

- **Synchronous:** Booking calls Flight and Passenger via gRPC
  (`src/Modules/Booking/src/GrpcClient`, served by `Flight` and `Passenger` GrpcServers).
- **Asynchronous:** integration events (e.g. `UserCreated` from Identity consumed by
  Passenger) via MassTransit — currently on the **in-memory transport**
  (`BuildingBlocks/MassTransit/Extensions.cs` uses `UsingInMemory`), which only works
  in a single process.

Once modules become separate processes, the in-memory transport no longer functions and
the gRPC endpoints must be network-addressable.

## Decision

- **Keep gRPC for synchronous request/response** calls: Booking → Flight (seat
  availability, reservation) and Booking → Passenger (passenger lookup). Service
  addresses are resolved via configuration/service discovery (Aspire locally,
  cluster DNS in Kubernetes) — see AB-237 for shared proto packages and addressing.
- **Replace the in-memory MassTransit transport with RabbitMQ** for all integration
  events (Identity → Passenger today; all future cross-service events) — see AB-235.
  The existing outbox/inbox patterns are retained per service for at-least-once
  delivery and idempotent consumption.
- **No direct service-to-service REST calls** and no shared MediatR in-process
  dispatching across service boundaries; all cross-service interaction goes through
  gRPC or the broker.

## Consequences

- MassTransit consumers/producers keep their code shape; only the transport
  configuration changes, minimizing migration churn.
- RabbitMQ becomes required infrastructure in every environment (it is already used by
  the integration-test containers, so operational knowledge exists).
- Synchronous coupling Booking → Flight/Passenger remains; timeouts, retries, and
  circuit breaking must be configured on the gRPC clients.
- Contracts (protos and event schemas) become published artifacts governed by ADR 0005.
