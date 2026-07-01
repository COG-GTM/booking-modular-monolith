# Flight Service (extracted microservice)

This is a **proof-of-concept extraction** of the `Flight` module out of the modular
monolith (`src/Api`) into its own independently-deployable ASP.NET Core host. The
`Flight` module is *not* removed from the monolith yet — both hosts currently reference
the same `Flight.csproj`. The goal is to prove the standalone-service pattern before
decommissioning the module from the monolith, and to establish a repeatable template for
extracting `Identity`, `Passenger`, and `Booking` next.

## Layout

```
src/Services/Flight/
├── Dockerfile                 # multi-stage build/publish of FlightService.csproj
├── README.md                  # this file
└── src/
    ├── FlightService.csproj   # web host; references Flight module + BuildingBlocks + ServiceDefaults
    ├── Program.cs             # AddSharedInfrastructure -> AddFlightModules -> UseFlightModules -> MapMinimalEndpoints
    ├── Extensions/
    │   └── SharedInfrastructureExtensions.cs  # Flight-only shared wiring (composite event mapper = FlightEventMapper only)
    ├── appsettings.json       # MessageBroker.Transport = RabbitMq
    └── appsettings.Development.json
```

## Cross-process messaging (RabbitMQ)

MassTransit transport is now **configuration-driven**. The transport is selected from the
`MessageBroker:Transport` configuration key (`RabbitMq` | `InMemory`), resolved in
`src/BuildingBlocks/MassTransit/Extensions.cs` (`AddCustomMassTransit(env, configuration, assemblies)`).
When the key is missing or unknown it falls back to **in-memory**, preserving the
monolith's original default.

- The **monolith** (`src/Api`) defaults to `InMemory` (see `src/Api/src/appsettings.json`).
- The **Flight service** defaults to `RabbitMq` so its integration events cross the
  process boundary.
- In orchestration (Aspire AppHost and docker-compose) **both** hosts are switched to
  `RabbitMq` and point at the same broker, so events published by one are consumed by the
  other. RabbitMQ credentials/host come from configuration (`RabbitMqOptions`), or from
  the Aspire-injected `ConnectionStrings:rabbitmq` when running under Aspire.

## Running

- **Aspire (recommended for local dev):** the AppHost (`src/Aspire/src/AppHost/Program.cs`)
  registers `flight-service` as a project resource, references the shared `rabbitmq`,
  `flightDb`, `mongo`, `eventstore`, and `persistMessageDb` resources, and exposes it on
  ports 4000 (https) / 4001 (http).
- **docker-compose:** `deployments/docker-compose/docker-compose.yaml` has a
  `flight-service` entry built from `src/Services/Flight/Dockerfile`. Infrastructure
  (Postgres, Mongo, EventStore, RabbitMQ) lives in
  `deployments/docker-compose/docker-compose.infrastructure.yaml`.
- **Standalone:** `dotnet run --project src/Services/Flight/src/FlightService.csproj`
  (requires the backing infrastructure to be reachable via the configured connection
  strings).

## Extracting the next module (Identity / Passenger / Booking)

Repeat this same pattern:

1. Create `src/Services/<Module>/src/<Module>Service.csproj` referencing
   `Modules/<Module>/src/<Module>.csproj`, `BuildingBlocks`, and `ServiceDefaults`.
2. Add a `Program.cs` that calls `AddSharedInfrastructure()` → `Add<Module>Modules()` →
   `Use<Module>Modules()` → `MapMinimalEndpoints()`.
3. Add a module-scoped `SharedInfrastructureExtensions` whose `CompositeEventMapper`
   contains only that module's `EventMapper`.
4. Add a `Dockerfile` copying only the project files that module needs (for layer
   caching).
5. Register the project in `booking-modular-monolith.sln`, the Aspire AppHost, and
   docker-compose, wiring the shared RabbitMQ broker into it.
6. Keep the module in the monolith until the standalone service is proven, then remove it.
