# Passenger.Host

Standalone ASP.NET Core host for the Passenger module (strangler-fig, ADR 0002).
The monolith (`src/Api`) keeps hosting all modules unchanged; this host runs the
Passenger module on its own — its own Kestrel endpoints, its own RabbitMQ queue
(`passenger-register-new-user-handler`), and its own Postgres database +
`persist_message` outbox/inbox (`passenger_modular_monolith`) and Mongo read
database (`passenger_modular_monolith_read`).

## Ports

| Endpoint        | Port | Protocol          |
|-----------------|------|-------------------|
| HTTP (REST)     | 5102 | http (Http1)      |
| gRPC            | 5202 | http (Http2, h2c) |
| `/health`       | 5102 | readiness+live    |

## Configuration

- `MessageBroker:ServiceName` = `passenger` → queue `passenger-register-new-user-handler`
  bound to the `UserCreated` exchange (Identity stays in the monolith).
- `PostgresOptions:ConnectionString:Passenger` — module write DB + outbox/inbox.
- `MongoOptions` / `MongoOptions:Passenger:DatabaseName` — module read DB.
- `Jwt` — tokens still issued by Identity in the monolith (`https://localhost:3000`).
  In containers, `Jwt:MetadataAddress` (host-local optional override, applied via `Configure<JwtBearerOptions>`
  before JWT bearer post-configuration builds the discovery manager) points OIDC metadata discovery at the monolith service
  (`http://booking_modular_monolith/.well-known/openid-configuration`); `Authority`
  stays unchanged so issuer validation still matches the issued tokens.
- `appsettings.docker.json` overrides hostnames to `postgres` / `mongo` / `rabbitmq`
  and binds Kestrel to `0.0.0.0`.

## Run

```bash
# infra (postgres, mongo, rabbitmq): see deployments/docker-compose
dotnet run --project src/Services/Passenger.Host
```

Docker (repo-root build context):

```bash
docker build -f src/Services/Passenger.Host/Dockerfile -t passenger-host .
docker run -p 5102:5102 -p 5202:5202 passenger-host
```
