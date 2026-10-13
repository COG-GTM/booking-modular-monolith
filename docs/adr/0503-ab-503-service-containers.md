# 0503. Per-service containers and multi-service docker-compose topology

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0002](0002-strangler-fig-migration-behind-gateway.md),
  [0004](0004-data-ownership-database-per-service.md), [0008](0008-service-host-layout-and-ports.md)

## Context

Each service host (Identity, Flight, Passenger, Booking) and the YARP gateway need their own image,
and developers need one command that runs the whole split topology locally. Until now
`deployments/docker-compose/docker-compose.yaml` ran a single monolith container next to the
infrastructure.

## Decision

- One multi-stage Dockerfile per deployable, built with the repository root as context:
  `src/Gateway/Dockerfile`, `src/Services/<Module>.Host/Dockerfile` (the paths the AB-490 service
  pipelines build). Runtime images are plain `mcr.microsoft.com/dotnet/aspnet:10.0`, HTTP only on
  container port 80, plus an HTTP/2-only gRPC port 81 for Flight and Passenger. No TLS
  certificates are baked into or mounted into the service images.
- `deployments/docker-compose/docker-compose.yaml` is the multi-service topology. It `include`s
  `docker-compose.infrastructure.yaml` (Postgres 16, Mongo, EventStoreDB, RabbitMQ, Redis and the
  observability stack) and adds `gateway` (host 5000) plus `identity-service` (5100),
  `flight-service` (5200/5201), `passenger-service` (5300/5301) and `booking-service` (5400).
- The gateway is repointed per module with
  `ReverseProxy__Clusters__<module>__Destinations__monolith__Address`; Booking reaches Flight and
  Passenger through `Grpc__FlightAddress=http://flight:81` / `Grpc__PassengerAddress=http://passenger:81`
  (network aliases `identity`, `flight`, `passenger`, `booking`).
- Each service connects with its own Postgres role to its own databases (created by
  `init/postgres/01-service-databases.sql`), its own Mongo read database, and RabbitMQ via the
  `MessageBroker` section. Booking alone uses EventStoreDB.
- Tokens are issued by Identity with the gateway's public URL as issuer
  (`AuthOptions__IssuerUri`/`Jwt__Authority` = `PUBLIC_GATEWAY_URL`, default
  `http://localhost:5000`); every host fetches discovery/JWKS from `identity-service` over the
  internal network (`Jwt__MetadataAddress`). Identity's audience is set to
  `booking-modular-monolith` so clients keep using the same access token they use with the monolith.
- Start-up ordering uses `depends_on: condition: service_healthy`: infrastructure healthchecks
  (pg_isready, mongosh ping, rabbitmq-diagnostics, EventStoreDB's built-in check), service probes
  over bash `/dev/tcp`, and a strict `/health` = 200 probe on the gateway.
- The monolith topology moves unchanged to `docker-compose.monolith.yaml`. Both files share the
  infrastructure file and therefore cannot run at the same time.
- `deployments/docker-compose/smoke-test.sh` drives the end-to-end flow through the gateway
  (token, register user, RabbitMQ-created passenger, flight + seat, booking over gRPC, seat
  reservation visible in Flight).

## Alternatives considered

- **Compose profiles in one file** instead of a separate monolith file: profiles cannot change
  the gateway's destinations per topology without duplicating the gateway service, and they make
  the default `docker compose up` ambiguous. Two files that share the infrastructure include are
  simpler.
- **Exposing every service through its own TLS endpoint**: requires certificates in every image
  and does not reflect how the services are deployed behind the gateway/ingress (AB-491).
- **Keeping Identity's `identity-api` audience**: would force clients to request a second scope
  just because the gateway now routes `/identity` to a different process.

## Consequences / operational and security notes

- Development-only credentials (Postgres roles, RabbitMQ `booking/booking`) live in compose
  defaults and can be overridden with `RABBITMQ_USER`/`RABBITMQ_PASSWORD`; real environments inject
  secrets (AB-491/Helm). RabbitMQ's `guest` user is loopback-only and is no longer used by containers.
- Service-to-service gRPC stays unauthenticated on the internal compose network (ARB follow-up from
  the epic). Service ports are published on the host for debugging only; clients must use the
  gateway.
- Service healthchecks only prove that Kestrel is serving (the hosts map `/health` in Development
  only); readiness of downstream dependencies is not checked yet (AB-495 observability).
- `postgres:latest` (18) is incompatible with the data-volume mount, so the image is pinned to 16
  with `max_prepared_transactions=100` (Flight's `TransactionScope`). Tempo is pinned to 2.7.2
  because the checked-in `tempo.yaml` uses the 2.x schema.
- No new cloud resources, vendors or runtime frameworks; images run the same .NET 10 runtime as
  the existing monolith image.
