# Gateway runbook (YARP single ingress)

The gateway (`src/Gateway/src`) is an opt-in ingress. The default entry point is still the unchanged
monolith. The gateway owns ingress concerns and forwards everything else unchanged to a cluster.

## Start locally

```bash
dotnet dev-certs https --trust
# Linux/OpenSSL clients may also need:
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs"
dotnet run --project src/Api/src
# In another terminal with the same trust environment:
dotnet run --project src/Gateway/src
```

Every local cluster uses `https://localhost:3000/`, the monolith's existing issuer endpoint.
No certificate-validation bypass is enabled. Aspire adds the gateway only with
`Gateway__Enabled=true dotnet run --project src/Aspire/src/AppHost` and routes to the API HTTPS endpoint.

### Compose limitation (deferred)

`docker compose -f deployments/docker-compose/docker-compose.yaml --profile gateway up -d`
opts in; default Compose startup is unchanged. The monolith still validates issuer
`https://localhost:3000` and does not set `AuthOptions:IssuerUri`. Inside the gateway container,
`localhost` is not the monolith; its service DNS name also does not match the development certificate.
The optional Compose gateway therefore retains HTTP destinations/discovery at
`http://booking_modular_monolith:80/` for routing scaffolding, but authenticated forwarding is
**not supported by this default Compose topology**: dynamically issued tokens do not match the
existing JWT issuer. Gateway authority intentionally remains the monolith's configured issuer.
Do not disable TLS verification or change monolith defaults to work around this.
End-to-end Compose auth and the issuer flip to the gateway URL are deferred to the route-flip PRs.

## Routes and clusters

Defined in `src/Gateway/src/appsettings.json` under `ReverseProxy`.

| Route | Match | Cluster | Auth at gateway |
| --- | --- | --- | --- |
| `identity-api` | `/api/{version}/identity/**` | `identity` | `ApiScope` |
| `flight-api`, `flight-api-root` | `/api/{version}/flight[/**]` | `flight` | `ApiScope` |
| `passenger-api` | `/api/{version}/passenger/**` | `passenger` | `ApiScope` |
| `booking-api`, `booking-api-root` | `/api/{version}/booking[/**]` | `booking` | `ApiScope` |
| `identity-connect`, `identity-discovery` | `/connect/**`, `/.well-known/**` | `identity` | anonymous (destination `Host`) |
| `api-fallback` | any other `/api/**` | `monolith` | `ApiScope` |
| `monolith-fallback` | everything else (`/`, `/swagger`, `/scalar`, ...) | `monolith` | anonymous |

`/health` and `/alive` are answered by the gateway itself. Backends still enforce their own
authorization; the gateway forwards the `Authorization` header untouched (defence in depth).
Requests with Content-Type `application/grpc*` are rejected with `404` before routing: internal gRPC
services are not public gateway endpoints. The monolith's direct unauthenticated gRPC exposure is
pre-existing and tracked separately; this gateway guard does not secure direct access to the monolith.

## Moving a route to an extracted service (config only)

Each cluster has one destination named `primary`. Override it per environment, no code or image change:

```bash
# Docker Compose / Kubernetes / Aspire environment variable
ReverseProxy__Clusters__flight__Destinations__primary__Address=http://flight-service:80/
```

YARP reloads `appsettings*.json` changes at runtime; environment variables apply on restart.
Rollback = remove the override (cluster falls back to the monolith address). Before a service takes over a
module, the route-flip PRs must add a switch to disable that module's background processing in the monolith:
only one owner may process the outbox. The proposed
`Modules__<Module>__BackgroundProcessingEnabled=false` setting is not implemented on this branch;
setting it here has no effect and is not sufficient for a safe cutover.

## Authentication

* Gateway and backends reuse `BuildingBlocks.Jwt` (`Jwt:Authority`, `Jwt:Audience=booking-modular-monolith`).
* The monolith configuration is unchanged. Local gateway authority and discovery use
  `https://localhost:3000`. Identity routes do not preserve the original Host: YARP sends the destination
  Host so IdentityServer derives its existing monolith issuer. One token validates at both gateway and API.
  Identity routes remove forwarded headers so an HTTP gateway caller cannot replace the backend's HTTPS scheme.
* `Jwt:MetadataAddress` optionally fetches discovery/JWKS from a different address; unset/empty retains
  authority-based discovery. It does not change the issuer. The issuer flip to the gateway URL belongs
  in the later route-flip PRs, not this slice.

```bash
curl -s -X POST http://localhost:5000/connect/token \
  -d grant_type=password -d client_id=client -d client_secret=secret \
  -d scope=booking-modular-monolith -d username=samh -d password='Admin@123456'
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/v1/flight/get-available-flights
curl -s -H "Authorization: Bearer $TOKEN" https://localhost:3000/api/v1/flight/get-available-flights
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
