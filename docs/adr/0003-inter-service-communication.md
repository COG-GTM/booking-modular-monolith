# 0003. Inter-service communication: gRPC (sync) + RabbitMQ via MassTransit (async)

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0001](0001-service-boundaries.md), [0006](0006-contract-and-proto-versioning.md),
  [0007](0007-unauthenticated-internal-grpc-accepted-risk.md); implemented by AB-494 (gRPC) and AB-501 (broker)

## Context

- **Sync:** Booking already calls Flight (`GetById`, `GetAvailableSeats`, `ReserveSeat`) and
  Passenger (`GetById`) over gRPC, configured by the `Grpc` options section
  (`FlightAddress`, `PassengerAddress`) in `src/Modules/Booking`.
- **Async:** integration events (e.g. Identity's `UserCreated` consumed by Passenger) flow through
  MassTransit. `BuildingBlocks/MassTransit` supports `TransportType.InMemory` and
  `TransportType.RabbitMq`, but the monolith uses InMemory, which cannot cross process boundaries.

## Decision

**Synchronous - gRPC (Booking -> Flight, Booking -> Passenger)**

- Keep gRPC for request/response calls where Booking needs an answer to proceed.
- Endpoints are config-driven: `Grpc:FlightAddress`, `Grpc:PassengerAddress`
  (env: `Grpc__FlightAddress`, ...). Addresses are plain `http://`/`https://` URIs - Grpc.Net.Client
  does not understand Aspire's `https+http://` service-discovery scheme.
- gRPC servers listen on a **dedicated HTTP/2-only port** (Flight 5201, Passenger 5301; port 81 in
  containers) because Kestrel cannot serve h2c gRPC on a plain-HTTP `Http1AndHttp2` endpoint.
- Protos live in one shared package `src/Contracts/Contracts.Grpc` (AB-494); clients and servers use
  the same `package` names (fixes today's `bookingFlight`/`flight` mismatch).
- Calls use deadlines and Polly retries for transient failures; no new sync dependencies between
  services without a new ADR.

**Asynchronous - RabbitMQ via MassTransit (Identity -> Passenger and all future events)**

- Integration events are published through each service's outbox and delivered over **RabbitMQ**.
- Transport is selected by a config-driven `MessageBroker` options section:

  ```json
  "MessageBroker": {
    "TransportType": "RabbitMq",
    "ServiceName": "passenger",
    "UseInbox": true,
    "RabbitMq": { "Host": "rabbitmq", "Port": 5672, "Username": "...", "Password": "...", "VirtualHost": "/" }
  }
  ```

  `TransportType` is `InMemory` | `RabbitMq`. **InMemory stays** for unit tests and for the monolith.
- Queues are named per service (prefixed with `ServiceName`) so each service gets its own copy of
  an event and competing consumers only compete within one service.
- Consumers are idempotent via the per-service inbox ([0004](0004-data-ownership-database-per-service.md)),
  keyed on MassTransit `context.MessageId`.
- RabbitMQ credentials are not `guest` (loopback-only) - set them via configuration/secrets.

## Alternatives considered

| Alternative | Pros | Cons | Why rejected |
| --- | --- | --- | --- |
| REST/HTTP for sync calls | Simpler tooling | Loses existing typed gRPC contracts; slower | gRPC already in place and works |
| Make Booking fully async (saga) | No temporal coupling | Large domain redesign of the booking flow | Out of scope for the migration; can follow later |
| Kafka / Azure Service Bus for async | Streaming/managed | New vendor/ops; MassTransit RabbitMQ support already exists | RabbitMQ is already supported by BuildingBlocks and compose |

## Consequences

- Booking depends on Flight and Passenger being available at request time (temporal coupling) -
  mitigated by deadlines, retries and clear error mapping.
- Identity -> Passenger becomes eventually consistent; a passenger appears shortly after user
  registration.
- RabbitMQ becomes a required piece of infrastructure for every service host.
