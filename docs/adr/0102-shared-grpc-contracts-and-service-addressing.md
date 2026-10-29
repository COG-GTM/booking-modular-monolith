# 0102. Shared gRPC contracts and config/discovery-driven service addressing

- **Status:** Proposed
- **Date:** 2026-10-29
- **ARB ticket:** TO BE CREATED
- **Authors:** Devin (AB-494)
- **Owning team:** Booking platform team
- **Related ADRs:** [0001](0001-yarp-api-gateway-single-ingress.md), [0100](0100-buildingblocks-shared-libraries.md), strangler-fig decisions (AB-499)

## Context

Booking calls Flight (`GetById`, `GetAvailableSeats`, `ReserveSeat`) and Passenger (`GetById`) over gRPC. The
protos were copied into three places (`Booking/GrpcClient/Protos`, `Flight/GrpcServer/Protos`,
`Passenger/GrpcServer/Protos`) and the Booking copies declared different packages (`bookingFlight`,
`bookingPassenger`) from the providers (`flight`, `passenger`). gRPC paths include the package
(`/flight.FlightGrpcService/GetById`), so the client and server copies described two different wire APIs: a
cross-process call from Booking would have returned `Unimplemented`. Booking's endpoints came from a flat
`Grpc:FlightAddress`/`Grpc:PassengerAddress` pair that no environment configured, the monolith exposed no h2c
endpoint, and a single Polly pipeline (3 retries, 3-minute timeout) wrapped every call - including the
non-idempotent `ReserveSeat`.

ARB triggers: **T4** cross-domain integration contract (shared wire contract between Booking, Flight and Passenger);
**T6** network boundary change (new HTTP/2 gRPC listener on the monolith and future hosts).

## Decision

1. **One proto source.** `src/Contracts/Contracts.Grpc` owns `flight.proto` and `passenger.proto` with the
   providers' packages (`flight`, `passenger`) and `option csharp_namespace` set to the existing namespaces
   (`Flight`, `Passenger`), so provider code is unchanged. It generates client and server stubs; Flight, Passenger
   and Booking reference the project (or the `BookingModularMonolith.Contracts.Grpc` package, SemVer `Version` in
   the csproj; the package also ships the `.proto` files). No other `.proto` may exist (enforced by a test).
2. **Compatibility gate.** `Contracts.Grpc.Tests` snapshots packages, RPC paths, field numbers/types and enum values.
   Additive changes update the snapshot and bump the minor version; breaking changes require a new package
   (`flight.v2`) served side by side.
3. **Addressing.** `Grpc:<Client>:Address` per client (`Flight`, `Passenger`). Defaults are logical
   (`http://_grpc.flight`), resolved by `Microsoft.Extensions.ServiceDiscovery` from `Services:<service>:grpc:0`
   (Aspire injects `services__flight__grpc__0`). Cluster/compose deployments either set that key or set the address
   to a concrete DNS name (`http://flight-service:81`); concrete URLs pass through unchanged. Aspire's
   `https+http://` scheme is rejected because `Grpc.Net.Client` has no resolver for it. Options are validated at
   startup.
4. **Resilience owned by gRPC.** The HTTP resilience handler that ServiceDefaults adds to every `HttpClient` is
   removed from these clients: it cannot see `grpc-status` trailers and would replay `ReserveSeat` on transport
   errors. Instead each channel gets a gRPC retry policy (`Unavailable` only, exponential backoff, `MaxAttempts`
   default 4) applied **only to idempotent reads** (`Flight.GetById`, `Flight.GetAvailableSeats`,
   `Passenger.GetById`), and an interceptor that applies a default deadline (10 s, configurable) to any call that
   does not set one. `ReserveSeat` is attempted exactly once.
5. **TLS.** Addresses may be `https://` (ALPN HTTP/2); `Grpc:RequireTls=true` makes startup fail on any plain-text
   address. In-cluster traffic stays h2c and unauthenticated for now (shared decision; mTLS/service identity is an ARB
   follow-up).
6. **Health-based readiness.** Servers expose `grpc.health.v1.Health` (overall and per service name). Booking
   registers `flight-grpc` / `passenger-grpc` health checks (tags `ready`, `grpc-dependency`) that call the
   dependency's health service with a short deadline, so the readiness endpoint `/health/ready` (mapped in every environment; every non-`live` check, so also the data stores when `HealthOptions:Enabled`) reports Unhealthy when a dependency is down. A
   server's own gRPC health status excludes `grpc-dependency` checks, so services never probe each other in a loop.
7. **Listeners.** h2c needs an HTTP/2-only endpoint: the monolith listens on `3002` (Development) / `81` (docker) for
   gRPC next to its HTTP/1.1 endpoints. Service hosts follow the same pattern.

## Alternatives considered

- **Keep per-module copies, fix the packages.** Cheap, but drift returns with the next edit; no compatibility gate.
- **Buf/BSR or a separate proto repository.** Better for polyglot estates; overkill while all consumers live in this
  repository. The NuGet package ships the `.proto` files, so moving to Buf later is non-breaking.
- **Keep the HTTP resilience handler.** Retries every RPC (including `ReserveSeat`) and only on HTTP-level failures;
  rejected.
- **Custom `https+http` resolver for Grpc.Net.Client.** Extra code for a scheme-selection feature we do not need;
  addresses state the scheme explicitly instead.

## Consequences

- Booking now talks the providers' real wire API; the old `bookingFlight`/`bookingPassenger` packages are gone (they
  were never served, so nothing depended on them).
- Generated namespaces for Booking changed from `BookingFlight`/`BookingPassenger` to `Flight`/`Passenger` (code
  only; no persisted or MassTransit names involved).
- A down dependency now makes Booking's readiness Unhealthy; operators must not wire the same check into liveness.
- Retries are bounded (`MaxAttempts` <= 5, deadline covers all attempts) and observable through the existing
  gRPC client OpenTelemetry instrumentation.

## Security

No auth on internal gRPC (shared AB-488 decision; follow-up ARB item: mTLS or token-based service identity). The new
gRPC listener must not be exposed through the public ingress - the gateway does not route it.

## Ownership and operations

See [docs/runbooks/grpc-service-addressing.md](../runbooks/grpc-service-addressing.md).
