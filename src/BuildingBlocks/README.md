# BuildingBlocks shared libraries

`src/BuildingBlocks` is split into focused libraries so a service host only takes the infrastructure it uses.
CLR namespaces are unchanged (`BuildingBlocks.<Area>`), so moving from the aggregate to focused references
needs no code changes. Decision record: [ADR 0100](../../docs/adr/0100-buildingblocks-shared-libraries.md).

| Project / package (`BookingModularMonolith.` prefix) | Depends on (BuildingBlocks) | Main third-party surface |
|---|---|---|
| `BuildingBlocks.Core` | - | MediatR, MassTransit.Abstractions, Google.Protobuf, Sieve |
| `BuildingBlocks.Constants` | - | - |
| `BuildingBlocks.Utils` | - | ASP.NET Core |
| `BuildingBlocks.Exception` | - | Grpc.Core.Api |
| `BuildingBlocks.Contracts` | Core | - |
| `BuildingBlocks.Contracts.Grpc` | - | Google.Protobuf, Grpc.Core.Api (shared Flight/Passenger wire contracts) |
| `BuildingBlocks.Grpc` | - | Grpc.Net.ClientFactory, Microsoft.Extensions.Http.Resilience |
| `BuildingBlocks.Logging` / `Caching` / `Polly` / `Mapster` | - | MediatR, EasyCaching, Polly, Mapster |
| `BuildingBlocks.Web` | Core, Utils | Asp.Versioning, Scrutor, Mapster |
| `BuildingBlocks.Validation` | Exception | FluentValidation |
| `BuildingBlocks.ProblemDetails` | Exception | ASP.NET Core (no EF Core) |
| `BuildingBlocks.OpenApi` | Web | Microsoft.AspNetCore.OpenApi, Scalar, Swashbuckle |
| `BuildingBlocks.Jwt` | Constants, Web | JwtBearer (no Duende) |
| `BuildingBlocks.EFCore` | Core, Web, Polly | EF Core, Npgsql |
| `BuildingBlocks.PersistMessageProcessor` | Core, EFCore, Utils, Web, Polly | MassTransit (outbox/inbox, `EfTxBehavior`, `ConsumeFilter<T>`) |
| `BuildingBlocks.Mongo` | Core, Web | MongoDB.Driver |
| `BuildingBlocks.EventStoreDB` | Core, Exception, Utils, Web | EventStore.Client |
| `BuildingBlocks.MassTransit` | Core, Exception, Web | MassTransit, RabbitMQ |
| `BuildingBlocks.HealthCheck` | Web | AspNetCore.HealthChecks.* |
| `BuildingBlocks.OpenTelemetryCollector` | Core, Web | OpenTelemetry |
| `BuildingBlocks.TestBase` | Core, EFCore, Mongo, PersistMessageProcessor, Web, Constants | xUnit, Testcontainers |
| `BuildingBlocks` (aggregate) | all of the above | host-level packages (Duende, YARP, Figgle, ...) |

The aggregate `BuildingBlocks.csproj` has no code. It exists so the monolith (`src/Api`) keeps building during the
strangler-fig transition; new hosts under `src/Services/<Module>.Host` must reference focused libraries only.
`tests/BuildingBlocks.Tests` fails the build if a library references the aggregate, if Core gains infrastructure
dependencies, or if the Booking module's graph picks up EF Core.

## Moved types

Namespaces did not change, but two types moved assembly to break cycles:

- `BuildingBlocks.EFCore.EfTxBehavior<,>` now lives in `BuildingBlocks.PersistMessageProcessor` (it needs the outbox DbContext).
- `BuildingBlocks.MassTransit.ConsumeFilter<T>` (inbox de-duplication) now lives in `BuildingBlocks.PersistMessageProcessor`.
- `ReplaceServiceWithSingletonMock<T>` (NSubstitute) moved from `BuildingBlocks.Web` to `BuildingBlocks.TestBase`.

## Event dispatching per service

`EventDispatcher` (Core) no longer depends on the persist-message processor, `IHttpContextAccessor` or a
host-composed mapper list:

```csharp
services.AddEventDispatcher();                    // Core: IEventDispatcher
services.AddEventMapper<BookingEventMapper>();    // each module registers only its own mapper(s)
services.AddHttpContextEventHeaders();            // Web: CorrelationId / UserId / UserName headers
builder.AddPersistMessageProcessor();             // PersistMessageProcessor: IIntegrationEventPublisher -> outbox
```

- `IEventMapper` registrations are enumerated and composed (`CompositeEventMapper`); first non-null mapping wins.
- `IIntegrationEventPublisher` is the sink for integration events and internal commands. `AddPersistMessageProcessor`
  registers the outbox-backed implementation; a host without an outbox can register its own.
- `IEventHeadersProvider` instances contribute envelope headers.

## Packaging (local NuGet feed, SemVer)

All libraries share `BuildingBlocksVersion` in [`Directory.Build.props`](Directory.Build.props) (SemVer 2.0; bump MAJOR on
breaking API / namespace changes, MINOR on additive APIs, PATCH on fixes). Package ids are
`BookingModularMonolith.<ProjectName>`.

```bash
scripts/pack-building-blocks.sh                 # -> artifacts/nuget/*.1.0.0.nupkg
scripts/pack-building-blocks.sh 1.1.0-preview.1 # explicit SemVer (validated)
scripts/verify-building-blocks-consumer.sh      # builds a Core+Web-only consumer from the feed, asserts no EF Core
dotnet nuget add source "$PWD/artifacts/nuget" --name local-buildingblocks   # consume from another repo/service
```

CI (`.github/workflows/building-blocks.yml`) builds every library on its own, runs the tests above, packs with
`1.0.0-ci.<run>` and runs the consumer check.

## gRPC clients

Flight and Passenger wire contracts live only in `Contracts.Grpc/Protos`. Keep field numbers stable;
use additive changes, and version the proto package (for example `flight.v2`) for breaking changes.

Register clients with `services.AddGrpcServiceClient<TClient>(configuration, "Flight")`.
`Grpc:Clients:Flight:Address` and `Grpc:Clients:Passenger:Address` are required absolute HTTP(S) URIs
(environment variables: `Grpc__Clients__Flight__Address`, `Grpc__Clients__Passenger__Address`).
Missing or invalid addresses fail startup with the configuration key in the error.

Resilience settings bind from `Grpc:Resilience`, then `Grpc:Clients:<Name>:Resilience` overrides individual values.
Built-in defaults are `MaxRetryAttempts: 3`, `AttemptTimeout: "00:03:00"`, `TotalTimeout: "00:03:00"`,
and `CircuitBreaker: { SamplingDuration: "00:02:00", FailureRatio: 0.1, BreakDuration: "00:00:05" }`.
The total timeout wraps retries, with the attempt timeout inside the circuit breaker. HttpClient's
own timeout is disabled so the configured pipeline owns timeout enforcement.
