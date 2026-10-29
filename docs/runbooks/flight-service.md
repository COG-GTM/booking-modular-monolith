# Runbook: standalone Flight service

`Flight.Host` runs the existing Flight module in its own process. It exposes REST and health endpoints on HTTP and Flight gRPC on a separate h2c listener. The module owns its migrations, seed data, outbox, and read-model setup.

## Ports and dependencies

| Endpoint | Local | Docker |
| --- | --- | --- |
| REST, OpenAPI, liveness, readiness | `http://localhost:5102` | `http://localhost:5104` |
| Flight gRPC and `grpc.health.v1.Health` | `http://localhost:5103` | `http://localhost:5105` |

The service needs PostgreSQL, MongoDB, and RabbitMQ. Local configuration uses the `flight_service` PostgreSQL role/database and `flight_service_read` Mongo database; Docker connects to Compose services `postgres`, `mongo`, and `rabbitmq`. Supply deployment credentials through the environment/secret manager rather than using development role-name passwords.

`/health/live` contains only liveness checks. `/health/ready` includes every non-liveness check and returns 503 when PostgreSQL, MongoDB, or RabbitMQ is unavailable. RabbitMQ is omitted from active probing when `MessagingOptions:Transport=InMemory`.

## Local run and smoke

```bash
docker compose -f deployments/docker-compose/docker-compose.yaml up -d postgres mongo rabbitmq eventstore
dotnet run --project src/Api/src/Api.csproj --launch-profile http
dotnet run --project src/Services/Flight.Host/Flight.Host.csproj
scripts/smoke/flight-host.sh
```

The monolith supplies local identity discovery/token issuance; Flight.Host validates the public issuer while reading metadata from the configured internal metadata address. The smoke script uses the development account and scope `booking-modular-monolith flight-api`, calls the seeded flight over REST and gRPC, requests available seats, reserves one, and checks both gRPC health service names.

## Docker

```bash
docker build -f src/Services/Flight.Host/Dockerfile -t flight-host:local .
docker run --rm --name flight-host --network booking \
  -p 127.0.0.1:5104:80 -p 127.0.0.1:5105:81 flight-host:local
```

The image selects the `docker` environment: HTTP is on container port 80 and HTTP/2-only h2c is on port 81. Verify `/health/ready` on port 5104 and gRPC `flight.FlightGrpcService` health on port 5105. Replace the example network name if the Compose project/network has been renamed.

Service-to-service h2c gRPC is unauthenticated under the shared epic decision (ARB follow-up); keep port 81 on the internal network and never expose it publicly.

## Running beside the monolith

During the AB-505 cutover, configure both hosts so each module's writes and background worker have one owner:

1. Set the monolith's `Modules__Flight__BackgroundProcessingEnabled=false`. Flight.Host's `Modules:Flight:BackgroundProcessingEnabled` remains true.
2. Route gateway Flight HTTP traffic to the standalone host:

   ```text
   ReverseProxy__Clusters__flight__Destinations__primary__Address=http://localhost:5102/
   ```

3. Route Booking's Flight gRPC client to the h2c listener:

   ```text
   Services__flight__grpc__0=http://localhost:5103
   ```

4. Use the standalone stores `flight_service` and `flight_service_read`; do not point the host at the monolith's `flight_modular_monolith` or `flight_modular_monolith_read` stores. Existing data migration/reconciliation is deferred to AB-505.
5. Run only one active Flight outbox/background processor for a given Flight store. Scale stateless HTTP replicas only when the worker switch is disabled on all but one replica.

Rollback by restoring the gateway's Flight destination and Booking's gRPC service address to the monolith, then re-enable monolith Flight background processing. Do not switch back to stale monolith databases after writes have started in the standalone stores; coordinate data reconciliation under AB-505.

## Verification

```bash
dotnet test src/Modules/Flight/tests/Host.Test
dotnet test src/Modules/Flight/tests/Unit.Test
dotnet test src/Modules/Flight/tests/Integration.Test
```

The host integration suite uses Testcontainers and also links the existing Flight integration cases against the standalone `Program`.
