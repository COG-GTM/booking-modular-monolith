# Identity.Host

`Identity.Host` runs the Identity module as a standalone ASP.NET Core service. It composes only the Identity module, shared service defaults, and focused BuildingBlocks; it does not load the Flight, Passenger, Booking, or monolith API projects.

The host owns the `identity_service` PostgreSQL database. Identity data and the Identity outbox share this database. The module publishes `UserCreated` through RabbitMQ, where the Passenger service can consume it.

## Configuration

Local defaults are in `appsettings.json`:

- `PostgresOptions:ConnectionString:Identity`: Identity service database.
- `AuthOptions:IssuerUri`: public issuer emitted by IdentityServer.
- `Jwt:Authority` and `Jwt:Audience`: issuer and audience used when validating bearer tokens.
- `MessagingOptions:Transport` and `RabbitMqOptions`: RabbitMQ transport and connection.
- `PersistMessageOptions:Interval` and `PersistMessageOptions:Enabled`: Identity outbox polling.
- `HealthOptions:Enabled`: enables PostgreSQL and RabbitMQ health probes.
- `ObservabilityOptions`: service and instrumentation names and telemetry exporters.
- `Modules:Identity:BackgroundProcessingEnabled`: enables the Identity outbox worker.

Environment variables can override these values using the standard double-underscore format, such as `AuthOptions__IssuerUri`.
`appsettings.docker.json` switches the database, broker, and OTLP endpoints to the Docker Compose network and points JWT discovery at the host's local port 80. Public issuer values remain overridable by deployment configuration.

## Run locally

Start PostgreSQL and RabbitMQ from the repository root:

```sh
docker compose -f deployments/docker-compose/docker-compose.yaml up -d postgres rabbitmq
```

The compose initialization script creates the `identity_service` database and login. Run the host from the repository root:

```sh
dotnet run --project src/Services/Identity.Host
```

The service listens on `http://localhost:5103`. Health endpoints are `/health` and `/alive`; the root endpoint returns the configured `AppOptions:Name`. The IdentityServer discovery document is at `/.well-known/openid-configuration`.

## Docker

Build from the repository root so the Dockerfile can copy its focused project sources:

```sh
docker build -f src/Services/Identity.Host/Dockerfile -t identity-host:local .
```

The container uses the `docker` environment and listens on port 80. Place it on the compose network with PostgreSQL, RabbitMQ, and the OTLP collector. Set `AuthOptions__IssuerUri` and `Jwt__Authority` to the externally visible issuer when the default local URL is not appropriate.
