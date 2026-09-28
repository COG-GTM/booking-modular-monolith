# Microservices split — migration notes

This repository is now a monorepo of four independently hostable services plus a gateway, sharing `src/BuildingBlocks`.
The module code moved 1:1 from `src/Modules/<Module>` to `src/Services/<Module>.Api`; namespaces, features, contracts and
tests are unchanged, only the host around them is new.

| Service | Project | Local (https/http) | Docker | Owns |
|---|---|---|---|---|
| Gateway (YARP) | `src/Api` | 3000 / 3001 | `gateway` 3001 | Routing only, no business code |
| Identity | `src/Services/Identity.Api` | 4000 / 4001 | `identity-api` (80, internal only) | Duende IdentityServer, `identity` Postgres db, publishes `UserCreated` |
| Flight | `src/Services/Flight.Api` | 4010 / 4011 (gRPC 81 in Docker) | `flight-api` (80 REST, 81 gRPC; internal only) | `flight` Postgres + `flight_read` Mongo, `FlightGrpcService` |
| Passenger | `src/Services/Passenger.Api` | 4020 / 4021 (gRPC 81 in Docker) | `passenger-api` (80 REST, 81 gRPC; internal only) | `passenger` Postgres + `passenger_read` Mongo, `PassengerGrpcService`, consumes `UserCreated` |
| Booking | `src/Services/Booking.Api` | 4030 / 4031 | `booking-api` (80, internal only) | EventStoreDB stream + `booking_read` Mongo, gRPC client of Flight and Passenger |

Every service also has its own `<service>_persist_message` Postgres database for the outbox/inbox store.

## What each host does

`ServiceDefaults.AddServiceHost(assembly, persistMessageConnectionName)` (`src/Aspire/src/ServiceDefaults/ServiceHostExtensions.cs`)
is the shared bootstrap used by all four `Program.cs` files:

- Aspire service defaults (OpenTelemetry, health checks, service discovery, resilience).
- `AddJwt()` — JWT bearer resource server. `Jwt:Authority` points at Identity, `Jwt:Audience` is the service's own scope
  (`flight-api`, `passenger-api`, `booking-api`, `identity-api`). Identity hosts IdentityServer *and* validates its own
  tokens for the `/api/v1/identity/*` endpoints.
- `AddCustomMassTransit(env, TransportType.RabbitMq, assembly)` — one RabbitMQ bus per service, scanning only that
  service's assembly for consumers. `RabbitMqOptions` is bound from configuration (`localhost` locally, `rabbitmq` in
  Docker/Aspire).
- `AddPersistMessageProcessor(name)` — per-service outbox/inbox.
- gRPC server + `GrpcExceptionInterceptor`, versioning, OpenAPI, ProblemDetails, EasyCaching.

Each service registers exactly one `IEventMapper` (its own) instead of the monolith's `CompositeEventMapper`.

## Gateway

`src/Api` is a YARP reverse proxy (`ReverseProxy` section in `appsettings*.json`). Routes preserve the existing shapes:

| Route | Cluster |
|---|---|
| `/api/{version}/flight/{**catch-all}` | Flight |
| `/api/{version}/passenger/{**catch-all}` | Passenger |
| `/api/{version}/booking/{**catch-all}` | Booking |
| `/api/{version}/identity/{**catch-all}`, `/connect/**`, `/.well-known/**` | Identity |

Under Aspire the cluster addresses are injected from service discovery (`ReverseProxy__Clusters__*__Destinations__*__Address`).

## Booking → Flight / Passenger (gRPC)

Booking keeps its own copies of `flight.proto` / `passenger.proto` and `GrpcOptions` (`Grpc:FlightAddress`,
`Grpc:PassengerAddress`). Locally these point at `https://localhost:4010/4020`, in Docker at `http://flight-api:81`
(plain-HTTP/2 port), under Aspire at the injected `https` endpoints. The `FlightGrpcService` contract is unchanged; the
contract tests below pin Booking's copy to Flight's.

## Observability

- `.AddAspNetCoreInstrumentation()`, `.AddGrpcClientInstrumentation()`, `.AddHttpClientInstrumentation()` and the
  MassTransit `DiagnosticHeaders.DefaultListenerName` source were already registered; gRPC and RabbitMQ hops therefore
  carry W3C `traceparent` automatically.
- New: `EventDispatcher` stores `traceparent`/`tracestate` in the persisted outbox envelope headers, and
  `PersistMessageProcessor` restores them as the parent of a `persist-message.outbox.publish` activity
  (source `BuildingBlocks.PersistMessageProcessor`, registered in `OpenTelemetryCollector`). Without this the background
  outbox publisher started a fresh trace, breaking the link between the HTTP request and the consumer in another service.

## Tests

- Per-service integration tests live in `src/Services/<Service>.Api/tests/*` and target `<Service>.Api.Program` through
  `BuildingBlocks/TestBase` (Testcontainers for Postgres, Mongo, RabbitMQ, EventStoreDB).
- `src/Services/Contract.Test`:
  - `Grpc/FlightGrpcSchemaCompatibilityTests` — descriptor-level comparison of Booking's proto copy (`flight` package, C# namespace `BookingFlight`)
    against Flight's `flight` proto (methods, field numbers/types/names, enums).
  - `Grpc/FlightGrpcBoundaryTests` — drives the real Flight host through Booking's generated client stub.
  - `Messaging/IntegrationEventSchemaTests` — freezes the MassTransit URN and property set of every
    `BuildingBlocks.Contracts.EventBus.Messages` event; new fields/events must be added as new versioned contracts.

## Cross-module in-process assumptions that break (flagged, not yet converted)

None of these are compile-time references — the modules were already separate assemblies — but they relied on
everything running in one process and now need an explicit event or gRPC contract:

1. **Identity → Passenger `UserCreated` is now at-least-once over RabbitMQ.**
   `Passenger/Identity/Consumers/RegisteringNewUser/V1/RegisterNewUser.cs` previously received the event in-process on the
   in-memory bus, effectively exactly-once and immediately. It now runs in another process with retries and redelivery;
   the consumer must be idempotent (dedupe on `UserCreated.Id`, currently only guarded by the inbox table) and
   `complete-registration` can race the consumer. Convert to: inbox dedupe by message id + tolerate "passenger not
   yet created" with a retry/redelivery policy.
2. **Booking → Flight `ReserveSeat` + EventStore append is not atomic.**
   `Booking/Booking/Features/CreatingBooking/V1/CreateBooking.cs` calls `ReserveSeat` on Flight over gRPC and then
   appends `BookingCreated` to EventStoreDB. When both lived in one process a failure surfaced as one HTTP 500 with the
   seat already reserved; across processes a network fault leaves the same inconsistency but with retries from Polly
   potentially reserving twice. This is the saga boundary (`FlightReserved`/`BookingFailed` compensation) that is
   deliberately **not** implemented in this change.
3. **Booking → Flight/Passenger gRPC lookups (`GetById`) are synchronous reads of another service's data.**
   Acceptable for the scaffold; the long-term option is to project the needed flight/passenger fields into Booking's
   Mongo read model from `FlightCreated/Updated/Deleted` and `PassengerCreated` events (contracts already exist, only
   carry `Id` — a richer event or a read-model projection via gRPC on receipt is needed).
4. **Caller identity is not forwarded on gRPC calls.**
   `Booking/Extensions/Infrastructure/GrpcClientExtensions.cs` does not attach `AuthHeaderHandler`, and Flight/Passenger
   map their gRPC services without `RequireAuthorization()`. In the monolith this was loopback; now the gRPC ports must
   be network-restricted (Docker exposes them only on the compose network) until token forwarding is added.
5. **One JWT audience for everything.**
   Identity's `ApiResource`s previously shared the `booking-modular-monolith` audience. Each service now validates its
   own audience, so clients must request `scope=flight-api passenger-api booking-api identity-api` (see `booking.rest`).
   Existing tokens issued for the old audience are rejected.
6. **Shared databases were split.**
   The single `booking_modular_monolith_read` Mongo database and single `persist_message` Postgres database are now
   per-service (`flight_read`, `passenger_read`, `booking_read`, `<service>_persist_message`). Existing data is not
   migrated; the outbox `TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(message.DataType)` lookup keeps
   working because each outbox only ever contains that service's own events.
7. **`AppDomain.CurrentDomain.GetAssemblies()`-style scanning.**
   MassTransit consumer registration and MediatR handler discovery were fed all module assemblies. Each host now scans
   only its own assembly, so a handler or consumer added to one module can no longer accidentally observe another
   module's commands/notifications. Anything that depended on that (there is none today) must become an integration
   event.
8. **Migrations and seed data run per host on startup.** Each service runs `UseMigration` against its own database;
   there is no longer a single startup that migrates everything, so bring services up in the order Aspire/Compose
   declares (`WaitFor`/`depends_on`).

9. **Token issuer vs. internal authority in Docker Compose.** Identity's `AuthOptions:IssuerUri` and the resource
   servers' `Jwt:Authority` are both `http://identity-api`, which only resolves inside the compose network, so
   discovery documents fetched through the gateway advertise an unreachable issuer. `BuildingBlocks.Jwt.AddJwt`
   uses one `Jwt:Authority` for metadata *and* `ValidIssuers`; splitting them (`Jwt:Issuer` = external gateway
   URL, `Jwt:MetadataAddress` = internal) is a follow-up. Aspire is unaffected because it pins a single https
   endpoint for both.

## Running

```bash
# infrastructure only
docker compose -f deployments/docker-compose/docker-compose.infrastructure.yaml up -d
# then each service (or `dotnet run --project src/Aspire/src/AppHost` for everything, dashboard on :18888)
dotnet run --project src/Services/Identity.Api/src
dotnet run --project src/Services/Flight.Api/src
dotnet run --project src/Services/Passenger.Api/src
dotnet run --project src/Services/Booking.Api/src
dotnet run --project src/Api/src
```

Full stack in containers: `docker compose -f deployments/docker-compose/docker-compose.yaml up --build`.
Only the gateway (`3001`) is published to the host; service REST/gRPC ports stay on the compose network. A fresh
`postgres-data` volume is seeded by `deployments/docker-compose/postgres/init-databases.sql` (per-service write and
outbox databases); the init script only runs on an empty data directory, so an existing monolith volume needs either
`docker compose down -v` or a one-off `docker exec -i postgres psql -U postgres < deployments/docker-compose/postgres/init-databases.sql`, and the broker runs with a non-guest user (`booking`/`booking`) because RabbitMQ refuses remote
`guest` logins.
