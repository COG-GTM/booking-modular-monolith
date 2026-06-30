# Flight Service (extracted microservice)

This is a proof-of-concept extraction of the **Flight** module from the modular
monolith (`src/Api`) into its own independently-deployable ASP.NET Core service.
The Flight module is still wired into the monolith host as well — this scaffold
exists to prove the standalone-service pattern before the module is decommissioned
from the monolith.

## What it is

`FlightService` is a thin host that references and registers **only** the Flight
module:

```
builder.AddSharedInfrastructure();   // JWT, MassTransit, gRPC, OpenAPI, persist-message, etc.
builder.AddFlightModules();          // the Flight module itself
...
app.UseFlightModules();
app.MapMinimalEndpoints();
```

Project references (mirrors what the Flight module needs):

- `src/Modules/Flight/src/Flight.csproj`
- `src/BuildingBlocks/BuildingBlocks.csproj`
- `src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj`

`Extensions/SharedInfrastructureExtensions.cs` is a service-local copy of the
monolith's shared infrastructure wiring, registering a `CompositeEventMapper`
with just the `FlightEventMapper` (the monolith registers all four module mappers).

## Cross-process messaging (RabbitMQ)

For integration events to cross process boundaries, MassTransit must use a real
broker instead of the in-memory transport. The transport is now **selectable via
configuration** (`BuildingBlocks/MassTransit/Extensions.cs`):

```
"MassTransitOptions": { "TransportType": "RabbitMq" },   // or "InMemory" (default fallback)
"RabbitMqOptions": { "HostName": "...", "Port": 5672, "UserName": "...", "Password": "..." }
```

`InMemory` remains the default fallback when nothing is configured. Both the
monolith and this service point at the same broker (set via env vars in
docker-compose / Aspire), so events published by one are consumed by the other.

## Running

- **Aspire** (`src/Aspire/src/AppHost`): the service is registered as
  `flight-service` and references the shared `rabbitmq`, `postgres` (flight +
  persist-message dbs) and `mongo` resources. Run the AppHost and use the
  dashboard.
- **docker-compose** (`deployments/docker-compose/docker-compose.yaml`): the
  `flight-service` entry builds from `src/Services/Flight/Dockerfile` and exposes
  port `4001`. Infrastructure (incl. RabbitMQ) is in
  `docker-compose.infrastructure.yaml`.
- **Standalone (dev)**: `cd src/Services/Flight/src && dotnet run`. Defaults to
  the in-memory transport; set `MassTransitOptions__TransportType=RabbitMq` to use
  a broker.

## Repeating the pattern (Identity / Passenger / Booking)

To extract another module, repeat:

1. Create `src/Services/<Module>/src/<Module>Service.csproj` referencing that
   module + `BuildingBlocks` + `ServiceDefaults`.
2. Add a `Program.cs` + service-local `SharedInfrastructureExtensions` that
   registers a `CompositeEventMapper` with only that module's event mapper.
3. Add a `Dockerfile` modeled on this one, copying only the needed project files.
4. `dotnet sln add` the new project.
5. Register it in the Aspire AppHost and docker-compose, referencing the shared
   RabbitMQ broker.
