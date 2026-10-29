# Aspire local orchestration

AppHost starts the **Services** topology by default. See [ADR 0497](../adr/0497-aspire-apphost-microservices-topology.md)
for the decision and boundaries. Use synthetic data only; these are development credentials and HTTP/h2c listeners.

## Prerequisites and startup

- .NET SDK selected by `global.json`, Docker running, and sufficient RAM for Elasticsearch/Kibana plus all service hosts.
- Restore tools and build from the repository root:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet tool restore
dotnet build booking-modular-monolith.sln
```

With the Aspire CLI installed/on PATH, run `aspire run` from the solution root. Alternatively:

```bash
cd src/Aspire/src/AppHost
dotnet run --no-build --launch-profile AppHost
```

The dashboard is `http://localhost:18888`; open the login-token URL printed by AppHost. Wait for all resources to be
Running and the five projects healthy before testing. Child services use Development settings; no child launch profile
is applied. Ports below must be free, including the services' HTTP/2 listeners.

### VM / host-only dashboard binding

The checked-in launch profile's dashboard OTLP endpoints use container DNS (`otel-collector`). On a VM where the dashboard
runs on the host, avoid those launch-profile values and specify loopback bindings explicitly (also avoids port 8080):

```bash
# From src/Aspire/src/AppHost, after the build above:
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:18888
export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:18889
export ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL=http://localhost:18890
export SSL_CERT_DIR=/etc/ssl/certs # Linux trust store, if required on the box
dotnet run --no-build --no-launch-profile
```

`--no-launch-profile` is intentional: launch-profile variables can override shell exports. The dashboard binds 18889/18890;
the separate collector remains on 4317/4318. Preserve the printed login token privately.

## Resources and ports

HTTP endpoints are non-proxied child listeners. Infra ports below are host-facing proxy ports; additional collector and
monitoring endpoints can be inspected in the dashboard.

| Project | HTTP | gRPC (HTTP/2-only h2c) | Health |
|---|---|---|---|
| gateway | 5000 | — | /health |
| identity | 5103 | — | /health |
| flight | 5107 | 5106 | /health/ready |
| passenger | 5102 | 5202 | /health/ready |
| booking | 5004 | — | /health/ready |
| api (Monolith only) | 3001; HTTPS 3000 | 3002 | /health |

| Infrastructure | Host ports / stores |
|---|---|
| PostgreSQL 16 | 5432; Services: identity_service, flight_service, passenger_service, booking_service |
| MongoDB | 27017; flight_service_read, passenger_service_read, booking_service_read |
| RabbitMQ | 5672; management 15672; local booking/booking secret parameters |
| EventStoreDB | 2113; legacy TCP 1113; Booking only in Services |
| Redis | TLS 6379; additional plain endpoint allocated by Aspire |
| Jaeger | UI 16686; collector 14268 / agent 6831 |
| Zipkin | 9411 |
| OTel collector | OTLP gRPC 4317 / HTTP 4318; metrics 8889 |
| Prometheus | 9090 |
| Grafana | 3000 in Services, 3005 in Monolith |
| Tempo 2.6.1 | 3200 / 9095; OTLP 4317 / 4318 (inspect dashboard bindings) |
| Loki | 3100 / 9096 |
| node-exporter | 9101 |
| Elasticsearch / Kibana | 9200 / 9300; Kibana UI 5601 |
| Aspire dashboard | 18888; VM OTLP bindings 18889 / 18890 |

Identity owns only its Postgres database and RabbitMQ. Flight and Passenger additionally use Mongo. Booking additionally
uses Mongo, EventStoreDB and downstream gRPC. Each owns its Postgres outbox. AppHost wires gateway destinations, metadata,
and gRPC discovery from endpoint expressions, not manually configured host addresses.

## Switching topology

Stop the current AppHost before switching. Append the topology setting to either dotnet command:

```bash
dotnet run --no-build --no-launch-profile -- --AppHost:Topology=Monolith
# Explicit default:
dotnet run --no-build --no-launch-profile -- --AppHost:Topology=Services
```

Use the VM environment exports above when choosing `--no-launch-profile`. Monolith starts only `api` and `gateway`;
all clusters point to `api`. Its physical Postgres databases are `identity`, `flight`, `passenger`, `booking`, with
resource names ending in `-db`. There is no data migration between topologies (AB-505). Local run-mode containers are
ephemeral; publish mode uses persistent volumes for configured stores. Do not rely on local run data surviving shutdown.

## Smoke flow and evidence

From the repository root, with Services healthy and `curl`/`jq` installed:

```bash
bash scripts/smoke/aspire-services.sh http://localhost:5000
```

Only the gateway URL is an input; every request goes through it. The script uses the seeded local admin password grant,
registers a unique user with a passport of at most ten characters, retries passenger completion by passport while the
RabbitMQ/outbox projection arrives, reads the returned passenger ID, fetches the seeded flight, then creates a booking.
Identity and Passenger IDs differ. Allow up to 150 seconds per projection stage; unexpected errors fail immediately.
The booking API's `{ "id": 0 }` is a stream revision, not the Booking aggregate ID.

Inspect PostgreSQL databases/tables and Mongo `*_service_read` databases in their containers; verify the smoke booking's
description (`Aspire services gateway smoke`) in `booking_service_read` and its `Booking-<id>` EventStoreDB stream.
In Jaeger (`http://localhost:16686`), select service `gateway`, operation POST booking, and inspect a trace containing
gateway → booking → flight/passenger gRPC. Named `aspire` exporters honor `OTEL_EXPORTER_OTLP_ENDPOINT`; the separate
collector exporter is configured through `ObservabilityOptions:OTLPOptions`. AppHost sets service names for correlation.

For a Monolith sanity check, verify `http://localhost:3001/health`, `http://localhost:5000/health`, and request a token via
the gateway using the same password-grant request as the smoke script. Do not run both topologies concurrently.

## Troubleshooting

- **Duplicate endpoint / frame-size errors:** launchSettings mirrors and Kestrel endpoints can create duplicate pinned
  ports or send HTTP/1 probes to h2c listeners. Keep child launch profiles excluded, only real endpoint annotations, and
  gRPC `Protocols=Http2`. Probe the HTTP readiness endpoint, not `Grpc`. Do not use `https+http://` for Grpc.Net.Client.
- **Docker Hub 429:** pre-pull a mirror image and tag it to the exact AppHost image name, for example:

  ```bash
  docker pull mirror.gcr.io/library/postgres:16
  docker tag mirror.gcr.io/library/postgres:16 docker.io/library/postgres:16
  docker pull mirror.gcr.io/grafana/tempo:2.6.1
  docker tag mirror.gcr.io/grafana/tempo:2.6.1 grafana/tempo:2.6.1
  ```

  Apply the same pattern to Mongo, RabbitMQ, EventStoreDB, Grafana/Prometheus, etc., using the exact tags in the dashboard.
  Elasticsearch uses `docker.elastic.co`, not a Docker Hub mirror. Do not replace the pinned Postgres/Tempo versions.
- **Port in use:** stop the previous topology first. API HTTPS needs 3000, so Monolith places Grafana on 3005. The VM
  dashboard command avoids the common 8080 collision. Check listeners with `ss -lntp` on Linux.
- **Startup failure:** inspect the dashboard console or `/tmp/aspire-dcp*/<resource>-*_out_*`. These files are temporary;
  copy needed evidence to a persistent home-directory location before shutdown.
- **Missing passenger:** expect asynchronous delivery, not identical Identity/Passenger IDs. Complete by passport and use
  the returned passenger ID; inspect RabbitMQ/outbox health if the retry deadline expires.
- **Stopping cleanly:** Ctrl+C in the foreground AppHost shell, or find the exact AppHost PID with `ps -eo pid,args` and
  send `kill -INT <pid>` / `kill -TERM <pid>`. Never use `pkill -f`. Allow DCP time to stop children and containers, then
  inspect `docker ps` and repository child processes. Do not stop unrelated workloads or delete persistent volumes.

## Model checks

```bash
dotnet test src/Aspire/tests/AppHost.Test/AppHost.Test.csproj
dotnet test src/BuildingBlocks/tests/BuildingBlocks.Tests/BuildingBlocks.Tests.csproj --filter FullyQualifiedName~OtlpEndpointTests
```

The AppHost tests inspect topology, database ownership, endpoint-derived environment, and invalid configuration without
starting Docker. They do not replace the live smoke flow or health checks.
