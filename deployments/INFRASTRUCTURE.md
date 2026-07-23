# Infrastructure Requirements

This document describes the infrastructure required to deploy and run the booking-modular-monolith, based on the Docker Compose definitions in [`deployments/docker-compose/`](./docker-compose) and the service configurations in [`deployments/configs/`](./configs).

## Overview

Two Docker Compose files are provided:

| File | Purpose |
|------|---------|
| [`docker-compose.yaml`](./docker-compose/docker-compose.yaml) | Full stack: all infrastructure services **plus** the application container (`booking_modular_monolith`), on the `booking` bridge network |
| [`docker-compose.infrastructure.yaml`](./docker-compose/docker-compose.infrastructure.yaml) | Infrastructure only (no application container), on the `infrastructure` bridge network — use this when running the API locally via `dotnet run` or Aspire |

## Host Prerequisites

- **Docker** and **Docker Compose** (Compose v2)
- **.NET 10 SDK** (only when running the API outside a container)
- An **ASP.NET Core HTTPS dev certificate** exported to `~/.aspnet/https/aspnetapp.pfx` with password `password` (mounted read-only into the app container — see [Config Certificate](../README.md#how-to-run))
- Linux `/dev/kmsg` device and read-only access to `/proc`, `/sys`, `/`, `/var/run`, and `/var/lib/docker` (required by cAdvisor and node-exporter)
- `memlock` ulimit set to unlimited for the Elasticsearch container

## Core Infrastructure Services (required)

These services are required by the application at runtime.

| Service | Image | Host Port(s) | Used For |
|---------|-------|--------------|----------|
| PostgreSQL | `postgres:latest` | 5432 | Write-side databases (Flight, Identity, Passenger) and outbox/inbox `persist_message` store |
| MongoDB | `mongo:latest` | 27017 | Read-side database (`booking_modular_monolith_read`) |
| EventStoreDB | `eventstore/eventstore:latest` | 2113 (HTTP) | Event sourcing for the Booking module |
| RabbitMQ | `rabbitmq:management` | 5672 (AMQP), 15672 (management UI) | Message broker for MassTransit |
| Redis | `redis` | 6379 | Caching (EasyCaching) |

### PostgreSQL

- Credentials: `postgres` / `postgres` (default; override via `POSTGRES_USER` / `POSTGRES_PASSWORD`)
- Custom server flags: `wal_level=logical` and `max_prepared_transactions=10`
- Persistent named volume: `postgres-data` mounted at `/var/lib/postgresql/data`
- Databases expected by the application (create them before first run):
  - `flight_modular_monolith`
  - `identity_modular_monolith`
  - `passenger_modular_monolith`
  - `persist_message`

### MongoDB

- Runs without authentication by default (root credentials are commented out in the compose files)
- Database: `booking_modular_monolith_read`
- No persistent volume is configured — data is lost when the container is removed

### EventStoreDB

- Single-node cluster (`EVENTSTORE_CLUSTER_SIZE=1`) with all projections enabled
- Runs **insecure** (`EVENTSTORE_INSECURE=True`); connection string `esdb://localhost:2113?tls=false`
- AtomPub over HTTP enabled
- No persistent volume is configured

### RabbitMQ

- Management plugin enabled (UI at `http://localhost:15672`, default `guest`/`guest`)
- No persistent volume is configured (a volume mount exists but is commented out)

## Observability Stack (optional but included)

The compose files also provision a full metrics/tracing/logging pipeline. The application exports telemetry via OTLP to the OpenTelemetry Collector, which fans out to the backends below (see [`configs/otel-collector-config.yaml`](./configs/otel-collector-config.yaml)).

| Service | Image | Host Port(s) | Purpose |
|---------|-------|--------------|---------|
| OpenTelemetry Collector | `otel/opentelemetry-collector-contrib:latest` | 4317 (OTLP gRPC), 4318 (OTLP HTTP), 8888/8889 (Prometheus metrics), 13133 (health check), 11888 (pprof), 55679 (zpages) | Central telemetry ingestion and routing |
| Prometheus | `prom/prometheus:latest` | 9090 | Metrics storage; remote-write receiver enabled so the collector can push metrics |
| Grafana | `grafana/grafana:latest` | 3000 | Dashboards (admin/admin); pre-provisioned datasources and dashboards from [`configs/grafana/`](./configs/grafana) |
| Jaeger (all-in-one) | `jaegertracing/all-in-one:latest` | 16686 (UI), 14317 (OTLP gRPC), 14318 (OTLP HTTP), 14268 (HTTP spans), 6831/udp (agent) | Distributed tracing |
| Zipkin | `openzipkin/zipkin:latest` | 9411 | Distributed tracing (alternative backend) |
| Tempo | `grafana/tempo:latest` | 24317 (OTLP gRPC), 24318 (OTLP HTTP), 3200 (internal) | Trace storage for Grafana; config in [`configs/tempo.yaml`](./configs/tempo.yaml) |
| Loki | `grafana/loki:latest` | 3100 | Log aggregation; config in [`configs/loki-config.yaml`](./configs/loki-config.yaml) |
| Elasticsearch | `docker.elastic.co/elasticsearch/elasticsearch:8.17.0` | 9200 (HTTP, override with `ELASTIC_HOST_PORT`), 9300 (transport) | Log storage (Serilog/Kibana); single-node, security disabled, 512 MB JVM heap, persistent `elastic-data` volume |
| Kibana | `docker.elastic.co/kibana/kibana:8.17.0` | 5601 (override with `KIBANA_HOST_PORT`) | Log visualization; depends on Elasticsearch |
| node-exporter | `prom/node-exporter:latest` | 9101 → 9100 | Host-level metrics |
| cAdvisor | `gcr.io/cadvisor/cadvisor:latest` | 8080 | Container-level metrics |

### Telemetry Pipelines (OTel Collector)

- **Traces**: OTLP in → Zipkin, Jaeger (OTLP gRPC), and Tempo (OTLP gRPC)
- **Metrics**: OTLP in → Prometheus (remote write to `http://prometheus:9090/api/v1/write` and Prometheus exporter on `:8889`)
- **Logs**: OTLP in → Loki (`http://loki:3100/otlp`) and Elasticsearch (`http://elasticsearch:9200`)

Prometheus scrapes the collector's exporter endpoints (`otel-collector:8888`, `otel-collector:8889`) and itself every 5–10 s (see [`configs/prometheus.yaml`](./configs/prometheus.yaml)).

## Application Container

Defined in `docker-compose.yaml` as `booking_modular_monolith`, built from [`src/Api/Dockerfile`](../src/Api/Dockerfile) (multi-stage, .NET 10 SDK → ASP.NET runtime).

| Setting | Value |
|---------|-------|
| Host ports | 3000 → 443 (HTTPS), 3001 → 80 (HTTP) |
| Environment | `ASPNETCORE_ENVIRONMENT=docker` |
| HTTPS certificate | `~/.aspnet/https` mounted at `/https` (read-only), `aspnetapp.pfx` with password `password` |

> **Known port conflict:** both Grafana (`3000:3000`) and the application container (`3000:443`) bind host port 3000 in `docker-compose.yaml`, so the full stack cannot start with both services as-is. Remap one of them (e.g. Grafana to `3300:3000`) before bringing up the app container alongside Grafana.

The application's default configuration ([`src/Api/src/appsettings.json`](../src/Api/src/appsettings.json)) expects the following endpoints:

| Dependency | Endpoint |
|------------|----------|
| PostgreSQL | `localhost:5432` (user `postgres` / password `postgres`) |
| MongoDB | `mongodb://localhost:27017` |
| EventStoreDB | `esdb://localhost:2113?tls=false` |
| OTLP exporter | `http://localhost:4317` (OTel Collector) |
| Aspire dashboard OTLP | `http://localhost:4319` |
| Zipkin exporter | `http://localhost:9411/api/v2/spans` |
| Jaeger exporter | `http://localhost:14317` (OTLP gRPC), `http://localhost:14268/api/traces` (HTTP) |
| JWT authority | `https://localhost:3000` (self-hosted IdentityServer) |

## Networking

- `docker-compose.yaml` creates a bridge network named `booking`
- `docker-compose.infrastructure.yaml` creates a bridge network named `infrastructure`
- All services join their respective network; only the listed ports are published to the host

## Persistent Volumes

| Volume | Used By | Contents |
|--------|---------|----------|
| `postgres-data` | PostgreSQL | Write-side databases |
| `elastic-data` | Elasticsearch | Log indices (full-stack compose only) |

All other services (MongoDB, EventStoreDB, RabbitMQ, Redis, Prometheus, Grafana, Loki, Tempo) are **ephemeral** — their data does not survive container removal. Add volumes for these services before using either compose file beyond local development.

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `ELASTIC_HOST_PORT` / `ELASTIC_PORT` | 9200 | Elasticsearch host/container port |
| `KIBANA_HOST_PORT` / `KIBANA_PORT` | 5601 | Kibana host/container port |

## Resource Considerations

- **Elasticsearch** is the heaviest service: 512 MB JVM heap (`ES_JAVA_OPTS`), unlimited `memlock`, and disk threshold checks disabled
- Running the full stack (18 containers) comfortably requires roughly **8 GB of RAM** and several GB of disk for images and volumes
- For a minimal development setup, start only the core services:

  ```bash
  docker-compose -f ./deployments/docker-compose/docker-compose.yaml up -d postgres mongo eventstore rabbitmq redis
  ```

## Security Notes (local development defaults)

The compose files are configured for **local development only**. Before any shared or production deployment:

- Replace default credentials (`postgres`/`postgres`, Grafana `admin`/`admin`, RabbitMQ `guest`/`guest`)
- Enable authentication on MongoDB and Redis
- Enable TLS on EventStoreDB (`EVENTSTORE_INSECURE=True` must be removed)
- Re-enable X-Pack security on Elasticsearch (`xpack.security.enabled=false` is set)
- Replace the shared dev HTTPS certificate and its hard-coded password
