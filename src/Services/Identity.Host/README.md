# Identity.Host (`identity-service`)

Standalone deployable host for the Identity module (`src/Modules/Identity`). It composes only
Identity: its minimal endpoints, `IdentityContext` (migrations + seed), Duende IdentityServer (the
token issuer), and its own persist-message outbox/inbox, which publishes `UserCreated` to RabbitMQ.
`src/Api` still composes the same module through the same `AddIdentityModules()` /
`UseIdentityModules()` extensions, so the monolith is unchanged.

## Run locally

```bash
# postgres:16 (max_prepared_transactions=100) with deployments/docker-compose/init/postgres
# applied (creates the identity_service role + identity_service / identity_service_persist_message DBs),
# and RabbitMQ on localhost:5672.
export SSL_CERT_DIR=$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs
dotnet run --project src/Services/Identity.Host
```

Migrations and the seed (user `samh` / `Admin@123456`, client `client` / `secret`) run at start-up.

Route the gateway to it (no gateway config change needed):

```bash
ReverseProxy__Clusters__identity__Destinations__primary__Address=http://localhost:5101/ \
  dotnet run --project src/Gateway
```

When tokens are minted through the gateway, run the host with the public issuer and point JWT
metadata at the host itself:

```bash
AuthOptions__IssuerUri=http://localhost:5000 \
Jwt__Authority=http://localhost:5000 \
Jwt__MetadataAddress=http://localhost:5101/.well-known/openid-configuration \
  dotnet run --project src/Services/Identity.Host
```

## Ports

| Where | Port |
| --- | --- |
| Local (`appsettings.json`, `launchSettings.json`) | `5101` (override with `Kestrel__Endpoints__Http__Url`) |
| Container (`ASPNETCORE_ENVIRONMENT=docker`) | `8080` |

## Endpoints

| Path | Notes |
| --- | --- |
| `POST /connect/token`, `/.well-known/*` | Duende IdentityServer |
| `POST /api/v1/identity/register-user` | requires a token with scope `booking-modular-monolith` |
| `GET /health/live` | process liveness (`self`) |
| `GET /health/ready` | liveness + `postgres-identity`, `postgres-identity-persist-message`, MassTransit bus |
| `GET /metrics` | Prometheus |

## Configuration keys

| Key | Default (local) |
| --- | --- |
| `PostgresOptions:ConnectionString:Identity` (fallback `ConnectionStrings:identity`) | `identity_service` DB / role |
| `PersistMessageOptions:ConnectionString:Identity` (fallback `ConnectionStrings:identity-persist-message`) | `identity_service_persist_message` DB |
| `Modules:Identity:BackgroundProcessingEnabled` | `true` (outbox dispatcher runs in this host) |
| `AuthOptions:IssuerUri` | `http://localhost:5101`; set to the gateway origin when fronted by it |
| `Jwt:Authority`, `Jwt:Audience`, `Jwt:MetadataAddress` | issuer above, `booking-modular-monolith`, unset |
| `Messaging:Transport`, `Messaging:AllowInMemory` | `RabbitMq` |
| `ConnectionStrings:rabbitmq` or `RabbitMqOptions:*` | `localhost:5672`, `guest` |
| `ObservabilityOptions:ServiceName` | `identity-service` |

MassTransit is registered with service name `identity-service` and scans only the Identity
assembly (Identity has no consumers today; it only publishes `UserCreated`).

## Docker

```bash
docker build -f src/Services/Identity.Host/Dockerfile -t identity-service .
```

## Running next to the monolith

Until cut-over (AB-505) the monolith still contains the Identity module. If both run against the
same broker, both publish `UserCreated` for users registered through them, and Passenger consumers
in the monolith and in `passenger-service` may both handle it. Give this host its own databases
(the defaults above), and route `/connect/*` + `/api/v*/identity/*` to only one of the two.

Tokens are signed with Duende's developer signing credential (`tempkey.jwk` and
`keys/is-signing-key-*.json` in the content root; both git-ignored). Every Identity instance that
should accept the others' tokens must share those files; see
[ADR 0013](../../../docs/adr/0013-identity-service-token-issuer.md).
