# Passenger service runbook

The Passenger service is an independently hosted composition of the existing
Passenger module. See [ADR 0104](../adr/0104-passenger-service-host.md) for the
decision and boundaries. Data migration and cutover of existing stores are
deferred to AB-505.

## Ports and endpoints

| Listener | Local development | Container |
| --- | --- | --- |
| HTTP / REST | `http://localhost:5102` | `http://<container>:80` |
| gRPC | `http://localhost:5202` | `http://<container>:81` |

- `GET /health/live`: process liveness only.
- `GET /health/ready`: Passenger PostgreSQL write database, MongoDB read
  database, and RabbitMQ readiness when RabbitMQ is selected.
- gRPC health service names: overall service (`""`) and
  `passenger.PassengerGrpcService`.
- `/` returns the configured `AppOptions:Name`.

## Configuration

| Key | Default / purpose |
| --- | --- |
| `AppOptions__Name` | `Passenger-Service` |
| `PostgresOptions__ConnectionString__Passenger` | Passenger-owned `passenger_service` database. `ConnectionStrings__passenger` takes precedence for the readiness probe. |
| `MongoOptions__ConnectionString` | `mongodb://localhost:27017`; module database is `MongoOptions__Passenger__DatabaseName` (`passenger_service_read`). |
| `Jwt__Authority`, `Jwt__Audience`, `Jwt__MetadataAddress` | JWT authority, audience, and discovery metadata used by REST authorization. |
| `MessagingOptions__Transport` | `RabbitMq`; consumer registration is Passenger-only. |
| `ConnectionStrings__rabbitmq` | Optional AMQP URI; takes precedence over `RabbitMqOptions`. |
| `RabbitMqOptions__HostName`, `RabbitMqOptions__Port`, `RabbitMqOptions__VirtualHost`, `RabbitMqOptions__UserName`, `RabbitMqOptions__Password` | Broker connection details; local defaults are `localhost`, `5672`, `/`, `booking`, `booking`. |
| `PersistMessageOptions__Enabled`, `PersistMessageOptions__Interval` | Outbox worker enablement and poll interval in seconds. |
| `Modules__Passenger__BackgroundProcessingEnabled` | Defaults to `true`; enables the Passenger outbox worker and consumer registration in this host. |
| `HealthOptions__Enabled` | `false` to prevent the shared health extension from adding duplicate/eager checks; host-local readiness checks remain enabled. |

The host resolves Passenger Mongo options using the module-specific settings,
then `ConnectionStrings__mongo`, then `MongoOptions__ConnectionString`. The
PostgreSQL health probe uses `ConnectionStrings__passenger` before
`PostgresOptions__ConnectionString__Passenger`. RabbitMQ readiness is registered
only when `MessagingOptions__Transport=RabbitMq` and resolves its URI lazily.

## Run locally

Start the infrastructure and run the service from the repository root:

```bash
docker compose -f deployments/docker-compose/docker-compose.infrastructure.yaml up -d postgres mongo rabbitmq
dotnet run --project src/Services/Passenger.Host/Passenger.Host.csproj
```

The development profile binds HTTP to 5102 and gRPC to 5202. Local PostgreSQL
initialization creates `passenger_service` and its service role on a fresh volume.

## Run alongside the monolith

To transfer Passenger queue ownership to the standalone host, disable Passenger
background processing in the monolith and leave it enabled in the Passenger
host:

```bash
docker compose -f deployments/docker-compose/docker-compose.infrastructure.yaml up -d postgres mongo rabbitmq eventstore
Modules__Passenger__BackgroundProcessingEnabled=false dotnet run --project src/Api/src/Api.csproj
dotnet run --project src/Services/Passenger.Host/Passenger.Host.csproj
```

The module setting applies independently to the monolith and standalone host:
`false` disables that host's Passenger outbox worker and MassTransit consumer
registration. Keep it `false` in the monolith and `true` in the standalone host;
do not run two active Passenger consumer sets against the shared queue.

Configure gateway routing to the local Passenger HTTP endpoint:

```bash
ReverseProxy__Clusters__passenger__Destinations__primary__Address=http://localhost:5102/
```

Configure the Booking module's Passenger gRPC address:

```bash
Services__passenger__grpc__0=http://localhost:5202
```

## Container

Build the service image:

```bash
docker build -f src/Services/Passenger.Host/Dockerfile -t booking-passenger-service .
```

After starting the infrastructure Compose stack, run the image on its
`infrastructure` network:

```bash
docker run --rm --name passenger-service \
  --network infrastructure \
  -p 5102:80 -p 5202:81 \
  booking-passenger-service
```

The `docker` environment settings target the Compose service names `postgres`,
`mongo`, and `rabbitmq`. Probe readiness with:

```bash
curl --fail http://localhost:5102/health/ready
```

## Scaling and data cutover

Run one replica with Passenger background processing enabled because the current
outbox polling implementation does not provide row locking. Do not automatically
copy or drop existing Passenger stores as part of deployment: draining pending
messages and migrating/rebuilding data are deferred to AB-505.
