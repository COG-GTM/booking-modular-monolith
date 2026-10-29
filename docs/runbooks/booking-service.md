# Runbook: standalone Booking service

Booking.Host is the standalone HTTP host for the existing Booking module. It exposes Booking API endpoints only and
calls Flight and Passenger over gRPC. See [ADR 0496](../adr/0496-standalone-booking-service-host.md) for the
architecture decision. Use the [cutover runbook](./cutover.md) for gateway ownership, data synchronization,
and rollback sequencing.

## Local setup

From the repository root, start the local dependencies:

```bash
docker compose -f deployments/docker-compose/docker-compose.infrastructure.yaml up -d postgres mongo eventstore rabbitmq
```

On a fresh PostgreSQL volume, `deployments/docker-compose/init/postgres/01-service-databases.sql` creates the
`booking_service` role and database with the local-only password `booking_service`. Initialization does not run again
for an existing volume. Verify the role/database before starting the host:

```bash
docker exec -e PGPASSWORD=booking_service postgres psql -h 127.0.0.1 \
  -U booking_service -d booking_service -c 'SELECT current_user, current_database();'
```

Start the host:

```bash
dotnet run --project src/Services/Booking.Host
```

The Development profile listens on `http://localhost:5004`. Local defaults use PostgreSQL `booking_service`,
MongoDB `booking_service_read`, EventStoreDB at `esdb://localhost:2113?tls=false`, and RabbitMQ with local
`booking/booking` credentials. Flight and Passenger gRPC discovery defaults to their service-host h2c endpoints at
`http://localhost:5106` and `http://localhost:5202`.

| Setting | Default | Override / purpose |
|---|---|---|
| `PostgresOptions:ConnectionString:Booking` | `booking_service` database and role | `PostgresOptions__ConnectionString__Booking`; the `ConnectionStrings:booking-persist-message` override takes precedence |
| `MongoOptions:ConnectionString` | `mongodb://localhost:27017` | `MongoOptions__ConnectionString` |
| `MongoOptions:Booking:DatabaseName` | `booking_service_read` | `MongoOptions__Booking__DatabaseName` |
| `EventStoreOptions:ConnectionString` | `esdb://localhost:2113?tls=false` | `EventStoreOptions__ConnectionString` or `ConnectionStrings__eventstore` |
| `Services:flight:grpc:0` | `http://localhost:5106` | `Services__flight__grpc__0` |
| `Services:passenger:grpc:0` | `http://localhost:5202` | `Services__passenger__grpc__0` |
| `Jwt:Authority` / `Jwt:MetadataAddress` | Local gateway and Identity host (`:5000` / `:5103`) | Override to the issuer and metadata endpoint for the target environment |
| `MessagingOptions:Transport` | `RabbitMq` | Set to `InMemory` only for tests |
| `Modules:Booking:BackgroundProcessingEnabled` | `true` | `false` disables both Booking's outbox poller and EventStoreDB subscription |
| `Modules:Booking:SubscriptionId` | `booking-service` | Unique EventStoreDB checkpoint/subscription identity |

Supply deployed credentials through environment variables backed by the platform secret store. Do not put production
secrets in appsettings or container images.

## Ownership changes

Use `scripts/cutover/cutover.sh flip booking service` to move Booking to this host and
`scripts/cutover/cutover.sh flip booking monolith` to roll back. The script freezes Booking,
quiesces the old worker, copies and verifies data, switches the gateway route, and resumes
only the active owner's background processing. See the [cutover runbook](./cutover.md)
for the full order and state recovery procedure.

Only one replica may run Booking background processing, and do not scale Booking.Host above one active processing
replica until the EventStoreDB subscription and outbox
concurrency model has changed. Keep `PersistMessageOptions:Enabled` enabled unless an intentional global outbox pause
is needed.

## Health probes

- `GET /health/live`: liveness-only `self` check; does not contact dependencies.
- `GET /health/ready`: readiness in every environment. It includes Booking PostgreSQL, Booking MongoDB,
  EventStoreDB, Flight gRPC, Passenger gRPC, and RabbitMQ when RabbitMQ is selected. It returns 503 if a required
  dependency is unhealthy.
- `GET /`: returns `Booking-Service`.

Booking.Host is an HTTP server and gRPC client; it does not expose a gRPC listener or `grpc.health.v1` server.

## Container

Build from the repository root:

```bash
docker build -f src/Services/Booking.Host/Dockerfile -t booking-service:local .
```

The Docker environment listens on port 80. It targets `postgres`, `mongo`, `eventstore`, `rabbitmq`, and the
`flight-service:81` / `passenger-service:81` gRPC addresses by default.

## Troubleshooting

| Symptom | Checks |
|---|---|
| Booking returns `Passenger not found` | Passenger may not yet have projected its registration event. Wait for the Passenger read model, then verify the Passenger gRPC address and health check. |
| `flight-grpc` or `passenger-grpc` is Unhealthy | Confirm `Services__flight__grpc__0` / `Services__passenger__grpc__0`, the provider's h2c port, and network reachability. The gRPC checks use short deadlines. |
| `booking-postgres` is Unhealthy or outbox is not draining | Verify `booking_service` exists, the connection override precedence, role permissions, `PersistMessageOptions:Enabled`, and that only one active outbox worker is running. |
| `booking-mongo` is Unhealthy or reads are missing | Check `MongoOptions:Booking:DatabaseName` and the Booking-specific Mongo connection; the host does not use a root-only connection check. |
| EventStoreDB projection is behind | Check `booking-eventstore`, the `booking-service` subscription logs/checkpoint, and whether background processing is enabled. A new checkpoint starts from the beginning; do not replace it casually. |
| Events/outbox work is duplicated | Ensure Booking background processing is enabled on only one host replica. |
| Token rejected | Confirm the token issuer matches `Jwt:Authority`, metadata is reachable at `Jwt:MetadataAddress`, and the token audience is `booking-modular-monolith`. |

Booking's read model is rebuilt from the shared EventStoreDB during cutover. Do not delete or repurpose
existing stores outside the documented rollback procedure.
