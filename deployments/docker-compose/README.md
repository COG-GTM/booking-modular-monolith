# Docker Compose

This directory defines the multi-service development topology:

- `docker-compose.yaml` runs the gateway, Identity, Flight, Passenger, and Booking service hosts. It
  includes `docker-compose.infrastructure.yaml`. The `booking_modular_monolith` service is opt-in
  through the `monolith` profile.
- `docker-compose.infrastructure.yaml` runs PostgreSQL, MongoDB, EventStoreDB, RabbitMQ, Redis, and
  the observability stack.

## Start the topology

From this directory, run:

```bash
docker compose up -d --build
```

To use a different cAdvisor host port when `8080` is occupied:

```bash
CADVISOR_HOST_PORT=18080 docker compose up -d --build
```

`RABBITMQ_USERNAME` and `RABBITMQ_PASSWORD` configure the broker and service hosts; both default to
`guest` for local development. Elasticsearch and Kibana host/container ports can be changed with
`ELASTIC_HOST_PORT`, `ELASTIC_PORT`, `KIBANA_HOST_PORT`, and `KIBANA_PORT`.

## Ports

The gateway is the only public entry point for application traffic. The service and gRPC debug
ports are bound to loopback. Infrastructure ports below are published by the infrastructure
Compose file for local development.

| Component | Host port(s) |
| --- | --- |
| Gateway | `5000` (public HTTP) |
| Identity service | `127.0.0.1:5101` |
| Flight service / gRPC | `127.0.0.1:5102`, `127.0.0.1:5112` |
| Passenger service / gRPC | `127.0.0.1:5103`, `127.0.0.1:5113` |
| Booking service | `127.0.0.1:5104` |
| PostgreSQL, MongoDB, EventStoreDB | `5432`, `27017`, `2113` |
| RabbitMQ / management | `5672`, `15672` |
| Redis | `6379` |
| Jaeger | `6831/udp`, `16686`, `14268`, `14317`, `14318` |
| Zipkin | `9411` |
| OpenTelemetry Collector | `11888`, `8888`, `8889`, `13133`, `4317`, `4318`, `55679` |
| Prometheus / node-exporter | `9090`, `9101` |
| Grafana | `3000` |
| Tempo | dynamically published container port `3200`; `24317`, `24318` |
| Loki | `3100` |
| Elasticsearch | `ELASTIC_HOST_PORT` (default `9200`), `9300` |
| Kibana | `KIBANA_HOST_PORT` (default `5601`) |
| cAdvisor | `CADVISOR_HOST_PORT` (default `8080`) |

REST is served inside each service container on port `8080`; Flight and Passenger gRPC use the
separate HTTP/2 h2c port `8081`. Their host debug mappings are loopback-only.

## Startup and health

Compose gates startup in this order: infrastructure health checks, then Identity/Flight/Passenger,
then Booking, then the gateway. Booking also waits for EventStoreDB, Flight, and Passenger. Use
`docker compose ps` to inspect health and status.

## Optional monolith

Start the monolith alongside the services with:

```bash
docker compose --profile monolith up -d booking_modular_monolith
```

Its module background processing is disabled so the service hosts remain the owners of outbox
polling and the Booking EventStoreDB subscription. Caveat: the monolith still binds its MassTransit
consumers (e.g. `monolith-register-new-user-handler` for `UserCreated`) regardless of that flag, so
while it runs a `UserCreated` is delivered to both it and `passenger-service`. Keep the profile for
short coexistence/rollback checks until consumer registration honours the module flag. To route a gateway cluster back to
the monolith, set
`ReverseProxy__Clusters__<c>__Destinations__primary__Address=http://booking_modular_monolith/`.

## Build one service image independently

Run a build from the repository root, replacing `<X>` and the image name as needed:

```bash
docker build -f src/Services/<X>.Host/Dockerfile -t booking-<service>:local .
```

For example:

```bash
docker build -f src/Services/Identity.Host/Dockerfile -t booking-identity-service:local .
```

## Data and signing keys

The PostgreSQL initialization SQL creates the service databases and roles only when PostgreSQL
starts with a fresh data volume. To recreate the databases from scratch, stop Compose and remove
its volumes with `docker compose down -v`; this deletes local database data.

The `identity-signing-keys` volume stores IdentityServer signing keys at `/app/keys`, keeping issued
tokens verifiable when the Identity container is recreated.
