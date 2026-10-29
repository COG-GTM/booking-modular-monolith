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

`/health` and `/alive` are answered by the gateway itself. Unknown application paths return
404 rather than falling through to a legacy host. Backends still enforce their own
authorization; the gateway forwards the `Authorization` header untouched. Authentication
middleware runs before route-freeze handling.

## Cutover overrides and route freeze

The optional `Cutover:ConfigFile` JSON file is loaded after the shipped settings and is
watched for changes. The Compose cutover overlay mounts a generated override file there.
`FrozenClusters` and `RetryAfterSeconds` are read through `IOptionsMonitor`; a frozen route
returns 503 problem+json with `Retry-After` and `X-Gateway-Cutover: frozen`, without
calling its backend.

```bash
scripts/cutover/cutover.sh flip flight service
scripts/cutover/cutover.sh flip flight monolith
```

See the [cutover runbook](./cutover.md) for the full freeze, worker quiescence, data-copy,
route-probe, and rollback procedure. Only one owner may run background processing for a
module at a time.

## Authentication

* The Identity host issues tokens with the gateway's public URL as issuer
  (`AuthOptions__IssuerUri=http://localhost:5000`); local discovery uses Identity on port 5103.

* Gateway and backends reuse `BuildingBlocks.Jwt` (`Jwt:Authority`, `Jwt:Audience=booking-modular-monolith`).
* `Jwt:MetadataAddress` lets each host fetch discovery/JWKS
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
