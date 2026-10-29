# Gateway runbook (YARP single ingress)

The gateway (`src/Gateway/src`) is the only public entry point. It owns ingress concerns and forwards
everything else unchanged to a cluster.

## Routes and clusters

Defined in `src/Gateway/src/appsettings.json` under `ReverseProxy`.

| Route | Match | Cluster | Auth at gateway |
| --- | --- | --- | --- |
| `identity-api` | `/api/{version}/identity/**` | `identity` | `ApiScope` |
| `flight-api`, `flight-api-root` | `/api/{version}/flight[/**]` | `flight` | `ApiScope` |
| `passenger-api` | `/api/{version}/passenger/**` | `passenger` | `ApiScope` |
| `booking-api`, `booking-api-root` | `/api/{version}/booking[/**]` | `booking` | `ApiScope` |
| `identity-connect`, `identity-discovery` | `/connect/**`, `/.well-known/**` | `identity` | anonymous (original `Host` kept) |
| `api-fallback` | any other `/api/**` | `monolith` | `ApiScope` |
| `monolith-fallback` | everything else (`/`, `/swagger`, `/scalar`, ...) | `monolith` | anonymous |

`/health` and `/alive` are answered by the gateway itself. Backends still enforce their own
authorization; the gateway forwards the `Authorization` header untouched (defence in depth).

## Moving a route to an extracted service (config only)

Each cluster has one destination named `primary`. Override it per environment, no code or image change:

```bash
# Docker Compose / Kubernetes / Aspire environment variable
ReverseProxy__Clusters__flight__Destinations__primary__Address=http://flight-service:80/
```

YARP reloads `appsettings*.json` changes at runtime; environment variables apply on restart.
Rollback = remove the override (cluster falls back to the monolith address). When a service takes over a
module, also turn off that module's background processing in the monolith
(`Modules__<Module>__BackgroundProcessingEnabled=false`) - only one replica may process the outbox.

## Authentication

* Local `dotnet run`: the `src/Api` launch profile already sets `AuthOptions__IssuerUri`/`Jwt__Authority=http://localhost:5000`
  and `Jwt__MetadataAddress=http://localhost:3001/...`, matching the gateway's `appsettings.json`; Compose and Aspire set the same.

* Gateway and backends reuse `BuildingBlocks.Jwt` (`Jwt:Authority`, `Jwt:Audience=booking-modular-monolith`).
* Tokens are issued with the gateway's public URL as issuer (`AuthOptions__IssuerUri` on the monolith) so they
  validate identically at the gateway and behind it. `Jwt:MetadataAddress` lets each host fetch discovery/JWKS
  from an internal address while validating the public issuer.

```bash
curl -s -X POST http://localhost:5000/connect/token \
  -d grant_type=password -d client_id=client -d client_secret=secret \
  -d scope=booking-modular-monolith -d username=samh -d password='Admin@123456'
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/v1/flight/get-available-flights
```

## Other settings

| Key | Default | Notes |
| --- | --- | --- |
| `Cors:AllowedOrigins` | `http://localhost:4200`, `http://localhost:5000` | Preflights are answered at the gateway. |
| `RateLimiting:Enabled` / `PermitLimit` / `WindowSeconds` / `QueueLimit` | `true` / `100` / `10` / `0` | Fixed window per JWT `sub`, else client IP. Rejections return `429` + `Retry-After`. Limits are per gateway replica. |
| `ObservabilityOptions:*` | OTLP + Prometheus | Same exporters as the monolith. |

## Troubleshooting

* Every response carries `correlationId`; the gateway log line
  `Gateway {Method} {Path} -> {StatusCode} ... (route=, cluster=, destination=, correlationId=, traceId=)` shows
  which cluster/destination served a request. The same `traceId` continues in the backend span (`traceparent`).
* `401` at the gateway with a valid token: check the token `iss` equals `Jwt:Authority` and the gateway can
  reach `Jwt:MetadataAddress`.
* `502`/`503`: the destination of the cluster in the log line is down or misconfigured.
* `429`: raise `RateLimiting:PermitLimit` or scale gateway replicas (stateless).
