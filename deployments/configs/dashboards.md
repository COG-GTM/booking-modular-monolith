# Dashboards

## Booking services observability

Start the local stack from the repository root:

```sh
docker compose -f deployments/docker-compose/docker-compose.infrastructure.yaml up -d postgres rabbitmq mongo eventstore otel-collector tempo loki prometheus grafana jaeger-all-in-one
```

| Service / `OTEL_SERVICE_NAME` | Local HTTP port | Compose HTTP port |
| --- | --- | --- |
| gateway | 5000 (set `ASPNETCORE_URLS=http://0.0.0.0:5000`) | 8080 |
| identity-service | 5101 | 8080 |
| flight-service | 5102 (gRPC: 5112) | 8080 |
| passenger-service | 5103 | 8080 |
| booking-service | 5104 | 8080 |

The host ports come from each service's `Properties/launchSettings.json`;
Gateway has no launch settings. For direct scraping from Docker, bind local
processes to `0.0.0.0`, not just localhost. Prometheus maps
`host.docker.internal` to the Docker host gateway on Linux. All five processes
expose anonymous `/health/live` (self only), `/health/ready` (registered
dependencies; Gateway currently self only), and `/metrics`.

`OTEL_SERVICE_NAME` overrides `ObservabilityOptions:ServiceName`, then the
application name. `OTEL_EXPORTER_OTLP_ENDPOINT` enables OTLP export and overrides
`ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint`. Use
`http://localhost:4317` for local processes and `http://otel-collector:4317` in
Compose. Set a distinct service name from the table for every process.

Grafana: <http://localhost:3000> (local demo credentials: admin/admin).
Open **Dashboards → Booking services** (`/d/booking-services`) and choose a
service. Panels use fixed `[5m]` ranges; generate traffic for several scrapes
before expecting rates. The default metrics source is `otel-collector`.
Direct compose/local scrape jobs are diagnostic alternatives: choose one
metrics source, never sum these copies together. Direct targets can be DOWN
when service hosts are not running. gRPC and MassTransit panels show no data
until the corresponding instruments emit metrics/spans.

The collector exports metrics only via its Prometheus endpoint `:8889` (with
resource attributes including `service_name`); it does not remote-write a second
copy. Tempo separately remote-writes generated service-graph/span metrics.
Logs use Loki's native OTLP endpoint and retain `trace_id` as structured metadata.

### Finding a trace

1. Send a CreateBooking or RegisterUser request through the Gateway. Note its
   `correlationId` response header.
2. In **Grafana Explore → Tempo**, search by service or TraceQL, for example
   `{ resource.service.name = "gateway" }` or
   `{ span.correlation.id = "<correlationId>" }`.
3. Open the trace. CreateBooking should include Booking, Flight and Passenger;
   RegisterUser should continue from Identity through the delayed outbox and
   RabbitMQ to Passenger (allow the worker polling interval).
4. Follow trace-to-logs or trace-to-metrics links. In **Explore → Loki**, use
   `{service_name="booking-service"} | trace_id = "<traceId>"`.
   The `trace_id` derived field links back to Tempo.

Jaeger also receives all collector traces: <http://localhost:16686>.
Tempo API/readiness: <http://localhost:3200/ready>; Loki:
<http://localhost:3100/ready>; Prometheus: <http://localhost:9090/-/ready>;
Grafana: <http://localhost:3000/api/health>; collector:
<http://localhost:13133/>. OTLP gRPC/HTTP ports are 4317/4318.

Tempo is pinned to 2.7.2 because its config is not compatible with Tempo 3.
Tempo, Prometheus and Grafana have wget healthchecks. Collector and Loki images
are distroless (no shell/wget/curl), so check their HTTP health endpoints from
the host instead. The provisioner loads dashboards from
`/var/lib/grafana/dashboards`; both dashboards and provisioning are mounted by
the infrastructure compose. Zipkin/Elasticsearch are not collector exporters
or provisioned datasources and are not needed for this stack.

### Docker Hub rate limits

If a pull returns HTTP 429, use the public mirror and retain the expected tag:

```sh
docker pull mirror.gcr.io/library/postgres:16
docker tag mirror.gcr.io/library/postgres:16 postgres:16
# For namespaced images: mirror.gcr.io/grafana/tempo:2.7.2, etc.
```

This is a local development stack, not a production deployment: demo passwords,
unencrypted internal OTLP, local storage and published ports need production
hardening before any deployment beyond the developer machine.

## Reference dashboards

- [Introducing ASP.NET Core metrics and Grafana dashboards in .NET 8](https://devblogs.microsoft.com/dotnet/introducing-aspnetcore-metrics-and-grafana-dashboards-in-dotnet-8/)
- [ASP.NET Core](https://grafana.com/grafana/dashboards/19924-asp-net-core/)
- [ASP.NET Core Endpoint](https://grafana.com/grafana/dashboards/19925-asp-net-core-endpoint/)
- [Node Exporter Quickstart and Dashboard](https://grafana.com/grafana/dashboards/13978-node-exporter-quickstart-and-dashboard/)
- [PostgreSQL Exporter Quickstart and Dashboard](https://grafana.com/grafana/dashboards/14114-postgres-overview/)
- [RabbitMQ-Overview](https://grafana.com/grafana/dashboards/10991-rabbitmq-overview/)