# BuildingBlocks packages

CLR namespaces are unchanged. Reference only the projects or packages a host needs.
The original `BuildingBlocks.csproj` is a source-compatibility aggregate, not the recommended
reference for an extracted service. Rebuild all consumers: this is not binary compatibility
with the former assembly.

## Dependency surface

| Package suffix | Responsibilities / direct shared-library dependencies |
| --- | --- |
| Core | Events, CQRS, aggregates, mapping, exceptions, utilities; no ASP.NET, EF, broker or persistence implementation |
| Web | ASP.NET helpers, endpoints, HTTP headers, gRPC exception adapter; Core |
| EFCore | Relational contexts, repositories, migrations; Core, Web, Polly |
| Mongo | Mongo contexts/repositories; Core, Web |
| EventStoreDB | Event sourcing, EventStoreDB client; Core, Web |
| PersistMessageProcessor | Durable inbox/outbox and EF transaction behavior; Core, EFCore, Web |
| MassTransit | RabbitMQ/in-memory transport, consumers; Core, Web, PersistMessageProcessor |
| Jwt | JWT validation/policies; Core, Web (no IdentityServer implementation) |
| Validation | FluentValidation pipeline; Core |
| ProblemDetails | Exception mapping (including EF concurrency); Core, Web, EF Core NuGet |
| HealthCheck | Infrastructure health probes; Core, Web, EFCore, Mongo, EventStoreDB, MassTransit |
| OpenTelemetryCollector | Tracing/metrics/logging instrumentation; Core, Web |
| TestBase | Existing Docker/ASP.NET fixtures and mocking helpers; Core, Web, Mongo, PersistMessageProcessor |
| Caching / Logging / Mapster / Polly | Existing single-purpose helpers; no shared-library references |
| OpenApi | ASP.NET OpenAPI UI helpers; ASP.NET shared framework |

The integration events and `IEvent` / `IIntegrationEvent` now live in the
`BookingModularMonolith.Contracts.Messages` package. Core references that package and forwards
the types for binary compatibility; Core is version 1.1.0.

Core retains MassTransit **abstractions** for existing event topology attributes and IDs,
and Protobuf for the existing typed envelope; it does not depend on a transport.
The aggregate retains TestBase for existing test consumers. Production service hosts should
avoid the aggregate, HealthCheck and TestBase if they need an EF-free dependency graph.
The monolith module `Booking.csproj` demonstrates Core/Web/Mongo/EventStoreDB without EF Core.
`EfTxBehavior` keeps its CLR namespace but is compiled in PersistMessageProcessor because it
depends on the durable message processor.

## Service-local dispatching

```csharp
services.AddEventMapper<MyModuleEventMapper>();
services.AddEventDispatcher();
services.AddScoped<IEventPublisher, MyPublisher>();
```

Core composes the registered `IEnumerable<IEventMapper>` (zero mappers is valid).
It publishes mapped integration events, wraps events marked `IHaveIntegrationEvent`, and
optionally maps internal commands when the caller supplies `typeof(IInternalCommand)`.
A mixed batch processes every event; a wrapped event does not suppress other mapped events.
Registrations are idempotent per mapper type. Use `AddWebEventDispatcher()` for HTTP
correlation/user headers, or provide an `IEventHeadersProvider`; Core's default headers are empty.
`AddPersistMessageProcessor(...)` registers the durable `IEventPublisher` adapter.
No publisher is silently supplied by Core: the host must choose its delivery semantics.

## Local NuGet feed

```sh
bash scripts/pack-buildingblocks.sh 1.0.0
dotnet restore MyService.csproj --source ./artifacts/packages --source https://api.nuget.org/v3/index.json
```

Package IDs are `BookingModularMonolith.BuildingBlocks.<Area>` (the aggregate is
`BookingModularMonolith.BuildingBlocks`) and `BookingModularMonolith.Contracts.Messages`,
targeting the repository's net10.0 runtime. The script argument overrides versions for the local
feed; individual packages use their declared SemVer when packed independently. The local folder
is the feed; nothing is published to a remote registry.

Run `bash scripts/test-buildingblocks-packages.sh 1.0.0` after packing to restore and execute
a package-only consumer with Core/Web/Mongo/EventStoreDB and assert its graph excludes EF Core.
That fixture is intentionally outside the solution because its packages must be packed first.
