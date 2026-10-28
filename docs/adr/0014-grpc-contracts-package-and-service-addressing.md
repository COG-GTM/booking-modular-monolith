# 0014. Shared gRPC contracts and service addressing

- **Status:** Proposed
- **Date:** 2026-10-28
- **ARB ticket:** TO BE CREATED
- **Authors:** AB-494 (epic AB-488)
- **Owning team:** TBD — owner to confirm before ARB
- **Related ADRs:** [0003](0003-sync-grpc-async-rabbitmq.md), [0005](0005-contract-and-proto-versioning-policy.md), [0006](0006-repository-layout-for-hosts-and-contracts.md), [0008](0008-defer-service-to-service-grpc-authentication.md)

## Context

Booking compiles duplicate Flight and Passenger proto files whose packages differ from the server
copies. The generated client methods consequently call `bookingFlight` and `bookingPassenger`
services that the servers do not implement. Booking also has no configured gRPC addresses and
inherits HTTP-level retries that can replay non-idempotent seat reservations.

## Decision

- Compile the single source of truth in `src/Contracts/Contracts.Grpc`; retain wire packages
  `flight` and `passenger`, while using the C# namespaces `Contracts.Grpc.Flight` and
  `Contracts.Grpc.Passenger`. This fixes the Booking `bookingFlight` → `flight` mismatch without
  changing wire messages, fields, or tags.
- Configure `Grpc:FlightAddress` and `Grpc:PassengerAddress`. The monolith defaults both to
  `https://localhost:3000`; Aspire injects its `api-https` endpoint. Extracted deployments may
  configure Kubernetes DNS service names such as `http://flight-service:81`; h2c requires an
  Http2-only listener.
- Retry gRPC `Unavailable` only for idempotent reads (Flight `GetById`, `GetAvailableSeats`, and
  Passenger `GetById`). Never retry `ReserveSeat`. Use a 10-second default deadline and remove
  HTTP resilience handlers from these clients to avoid protocol-agnostic retries and timeouts.
- Expose `grpc.health.v1` on the monolith, mapping its service names to liveness checks only.
  Booking performs separate readiness checks for the Flight and Passenger services.
- HTTPS uses platform certificate validation. Service-to-service calls remain unauthenticated as
  deferred in ADR 0008.

## Alternatives considered

| Alternative | Why rejected |
| --- | --- |
| Keep per-service proto copies | Copies drifted and changed the wire package. |
| Ship proto files only and compile per consumer | Every consumer could still generate incompatible namespaces or package names. |
| Polly/HTTP-level retries | HTTP retries can replay `ReserveSeat` and do not understand gRPC status codes or method idempotency. |
| Client-side load balancing with `dns:///` | Adds resolver and deployment-specific behavior; service discovery plus configured addresses meets current needs. |

## Consequences

- Positive: clients and servers share one wire contract; addresses are deployment configuration;
  retry policy and readiness behavior are explicit.
- Risks: extracted hosts must configure an address and serve HTTP/2; the default self-address is
  monolith-specific.
- Follow-ups: create the ARB ticket; configure `Grpc:FlightAddress` and `Grpc:PassengerAddress`
  when adding extracted service hosts. The monolith container must advertise a reachable HTTPS
  self-address (or an HTTP/2-only h2c endpoint) rather than `localhost` if the API listens on a
  different container interface.
