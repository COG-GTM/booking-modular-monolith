# Runbook: gRPC contracts and service addressing

Booking -> Flight / Passenger gRPC (ADR [0101](../adr/0102-shared-grpc-contracts-and-service-addressing.md)).

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Grpc:Flight:Address` | `http://_grpc.flight` | Logical (service discovery) or concrete `http(s)://host:port` |
| `Grpc:Passenger:Address` | `http://_grpc.passenger` | " |
| `Services:flight:grpc:0` | `http://localhost:3002` (docker: `http://localhost:81`) | Discovery entry for `_grpc.flight` |
| `Services:passenger:grpc:0` | same | Discovery entry for `_grpc.passenger` |
| `Grpc:<Client>:Deadline` | `00:00:10` | Default deadline (covers all retry attempts) |
| `Grpc:<Client>:MaxAttempts` | `4` | Attempts for idempotent reads on `Unavailable` (1-5; 1 disables) |
| `Grpc:<Client>:InitialBackoff` / `MaxBackoff` | `200ms` / `2s` | Exponential backoff (x2) |
| `Grpc:<Client>:HealthCheckTimeout` | `00:00:02` | Readiness probe deadline |
| `Grpc:RequireTls` | `false` | Fail startup on any `http://` address |

Environment variables use `__`, e.g. `Grpc__Flight__Address=http://flight-service:81` or
`services__flight__grpc__0=http://flight-service:81`. `ReserveSeat` is never retried.

| Environment | How Booking finds Flight/Passenger |
|---|---|
| `dotnet run` (Development) | `appsettings.json` discovery entries -> monolith h2c listener `http://localhost:3002` |
| Aspire AppHost | AppHost injects `services__flight__grpc__0` / `services__passenger__grpc__0` from the `grpc` endpoint |
| Docker image | `appsettings.docker.json` -> `http://localhost:81` (same container) |
| Kubernetes / compose with split hosts | `services__flight__grpc__0=http://flight-service:81` (or set `Grpc__Flight__Address` directly) |

When a module moves to `src/Services/<Module>.Host`, only these values change; Booking code does not.

## Listeners

h2c gRPC needs an HTTP/2-only Kestrel endpoint (`Kestrel:Endpoints:Grpc`, `Protocols=Http2`): `3002` in Development,
`81` in the docker image. HTTP/1.1 APIs stay on `3000`/`3001` (`80` in docker). Do not route the gRPC port through
the gateway.

## Health

- `grpc.health.v1.Health/Check` on the gRPC listener: service `""`, `flight.FlightGrpcService`,
  `passenger.PassengerGrpcService`. Excludes checks tagged `grpc-dependency`.
- `/health/ready` (every environment; use it as the readiness probe) runs every check except the liveness-only
  (`live`) ones: `flight-grpc`, `passenger-grpc`, plus Postgres/Mongo/RabbitMQ/EventStoreDB when `HealthOptions:Enabled`;
  503 Unhealthy when any of them fails (gRPC dependency unreachable or not `SERVING`). `/health` (all checks) and
  `/alive` (liveness, never dependencies) stay Development-only.
- Docker Compose: `appsettings.docker.json` declares `Kestrel:Endpoints` (http :80, h2c :81), which replaces
  `ASPNETCORE_URLS`, so the compose service adds `Kestrel__Endpoints__Https__Url=https://+:443` next to the mounted cert.

## Verify

```bash
# grpc.health.v1 has no reflection here: pass the proto (grpc/grpc-proto: grpc/health/v1/health.proto)
grpcurl -plaintext -import-path . -proto grpc/health/v1/health.proto \
  -d '{"service":"flight.FlightGrpcService"}' localhost:3002 grpc.health.v1.Health/Check   # {"status":"SERVING"}
grpcurl -plaintext -import-path src/Contracts/Contracts.Grpc/Protos -proto flight.proto \
  -d '{"FlightId":"<flight id>"}' localhost:3002 flight.FlightGrpcService/GetAvailableSeats
curl -s -w ' %{http_code}\n' http://localhost:3001/health/ready  # Healthy 200 / Unhealthy 503
# Failure drill: services__passenger__grpc__0=http://localhost:3999 -> POST /booking fails Unavailable after
# 4 Passenger.GetById attempts (~1.5 s) and /health/ready is 503; a non-responding Flight -> DeadlineExceeded at Grpc:Flight:Deadline.
```

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Startup fails with `Grpc:<Client>:Address must ...` | Missing/invalid address or `https+http://` scheme; set a plain `http(s)://` URL |
| `Unimplemented` | Client and server on different contract versions/packages; both must use `Contracts.Grpc` |
| `Unavailable` after retries | Dependency down or wrong port; check `services__<svc>__grpc__0` and the h2c listener |
| `DeadlineExceeded` | Dependency slow; raise `Grpc:<Client>:Deadline` only after checking the provider |
| HTTP/1.1 `protocol error` on h2c | Endpoint is `Http1AndHttp2` over plain text; use an `Http2`-only endpoint |
| Unknown health status | Server has no health checks mapped to the requested service name |
