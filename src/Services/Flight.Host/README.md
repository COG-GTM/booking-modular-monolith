# Flight.Host

`Flight.Host` is the standalone process boundary for the Flight module. It composes the existing Flight module and shared host infrastructure only; Flight continues to own its persistence, migrations, seed data, endpoints, read model, and gRPC service registrations.

## Listeners

| Protocol | Local | Docker |
| --- | --- | --- |
| HTTP REST, OpenAPI, health | `http://localhost:5102` | `http://localhost:5104` |
| h2c gRPC | `http://localhost:5106` | `http://localhost:5105` |

The gRPC listener is HTTP/2-only because h2c does not negotiate HTTP/2 when a clear-text endpoint also supports HTTP/1.1.
Service-to-service h2c gRPC is unauthenticated under the shared epic decision (ARB follow-up); keep port 81 on the internal network and never expose it publicly.

## Configuration

Local defaults are in `appsettings.json`; the Docker profile is `appsettings.docker.json`.

| Key | Purpose |
| --- | --- |
| `PostgresOptions:ConnectionString:Flight` / `ConnectionStrings:flight` | Flight database and transactional outbox |
| `MongoOptions:ConnectionString`, `MongoOptions:Flight:DatabaseName` | Flight read-model database |
| `MessagingOptions:Transport`, `RabbitMqOptions:*`, `ConnectionStrings:rabbitmq` | Outbox publishing transport and broker |
| `Jwt:Authority`, `Jwt:Audience`, `Jwt:MetadataAddress` | Bearer-token validation and discovery |
| `PersistMessageOptions:Interval`, `PersistMessageOptions:Enabled` | Outbox worker interval and global enablement |
| `Modules:Flight:BackgroundProcessingEnabled` | Flight-specific outbox worker switch |
| `HealthOptions:Enabled` | Kept false so monolith-wide probes are not registered |
| `ObservabilityOptions:*` | Service name, exporters, and telemetry endpoints |

The `/health/live` endpoint reports the built-in liveness check. `/health/ready` and the gRPC health service include PostgreSQL, MongoDB, and RabbitMQ readiness (RabbitMQ is skipped for the InMemory transport). Readiness is mapped in every environment; OpenAPI UI is Development-only.

## Run locally

Start the local dependencies (including RabbitMQ):

```bash
docker compose -f deployments/docker-compose/docker-compose.yaml up -d postgres mongo rabbitmq eventstore
```

Run the identity-enabled monolith for local token issuance, then start Flight.Host:

```bash
dotnet run --project src/Api/src/Api.csproj --launch-profile http
dotnet run --project src/Services/Flight.Host/Flight.Host.csproj
```

The local settings use the dedicated `flight_service` PostgreSQL database and `flight_service_read` Mongo database. To smoke-test HTTP and gRPC endpoints, see [`scripts/smoke/flight-host.sh`](../../../scripts/smoke/flight-host.sh).

## Build and test

```bash
dotnet build src/Services/Flight.Host/Flight.Host.csproj
dotnet test src/Modules/Flight/tests/Host.Test
dotnet test src/Modules/Flight/tests/Unit.Test
dotnet test src/Modules/Flight/tests/Integration.Test
```

Build and run the standalone image from the repository root:

```bash
docker build -f src/Services/Flight.Host/Dockerfile -t flight-host:local .
docker run --rm --name flight-host --network booking \
  -p 127.0.0.1:5104:80 -p 127.0.0.1:5105:81 flight-host:local
```

For side-by-side routing, worker ownership, and rollback guidance, see the [Flight service runbook](../../../docs/runbooks/flight-service.md) and [ADR 0502](../../../docs/adr/0502-standalone-flight-service-host.md).
