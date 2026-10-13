# Observability, health checks and distributed tracing

Every deployable (gateway, identity-service, flight-service, passenger-service, booking-service, and the
monolith during transition) gets the same telemetry and probes from `AddServiceDefaults()` /
`UseServiceDefaults()` (`src/Aspire/src/ServiceDefaults`), which call `AddCustomObservability()` /
`UseCustomObservability()` (`BuildingBlocks.OpenTelemetryCollector`) and `AddCustomHealthCheck()` /
`UseCustomHealthCheck()` (`BuildingBlocks.HealthCheck`).

## service.name

| Deployable | `service.name` |
|---|---|
| `src/Gateway` | `gateway` |
| `Identity.Host` | `identity-service` |
| `Flight.Host` | `flight-service` |
| `Passenger.Host` | `passenger-service` |
| `Booking.Host` | `booking-service` |

Resolution order: `OTEL_SERVICE_NAME` (set by Aspire/compose/Kubernetes) > `ObservabilityOptions:ServiceName`
> application name. The resource also carries `service.version`, `service.instance.id` (machine name) and
`deployment.environment.name`.

## Export

All three signals go over OTLP gRPC to the collector (`deployments/configs/otel-collector-config.yaml`), which
fans out to Jaeger + Tempo + Zipkin (traces), Prometheus (remote write and `:8889`) and Loki + Elasticsearch
(logs). Endpoint: `ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint`, overridable with the standard
`OTEL_EXPORTER_OTLP_ENDPOINT` (e.g. `http://otel-collector:4317` in compose). The older misspelled key
`OTLPGrpExporterEndpoint` is still bound as an alias. Each service additionally exposes `/metrics`
(`UsePrometheusExporter`), scraped by the `services` job in `deployments/configs/prometheus.yaml`.

Grafana dashboard **Booking services - per-service overview**
(`deployments/configs/grafana/dashboards/booking-services.json`) shows RED metrics, outgoing HTTP/gRPC calls,
runtime and Loki logs for the selected `service` (Prometheus `job` = `service.name`, Loki `service_name`).

## Trace propagation

W3C trace context (`traceparent`/`tracestate`) end to end:

- **gateway -> service (REST)**: ASP.NET Core server span in the gateway; YARP forwards through an instrumented
  HttpClient that injects `traceparent`; the downstream ASP.NET Core instrumentation continues it.
- **gRPC (Booking -> Flight/Passenger)**: gRPC client instrumentation + HttpClient injection; server side is the
  ASP.NET Core instrumentation of the gRPC endpoint.
- **RabbitMQ (MassTransit)**: the `MassTransit` ActivitySource is listened to; MassTransit writes the trace
  context into message headers on send and restores it on consume.
- **Outbox**: messages are published later by the outbox worker, outside the request. The processor stores the
  writer's `traceparent`/`tracestate` in the persisted envelope headers and starts the
  `BuildingBlocks.PersistMessageProcessor` "outbox publish" span with that parent, so the MassTransit send and
  every consumer stay in the original request's trace.

Health, alive and `/metrics` requests are not traced.

## Health endpoints

Mapped in every environment, anonymous:

| Path | Checks | Use |
|---|---|---|
| `/health/live` | `self` only (tag `live`) | liveness probe — restart on failure |
| `/health/ready` | all checks, JSON body per check | readiness probe — remove from load balancing |
| `/health`, `/alive` | all / `live` | kept for the Aspire AppHost and existing probes |

Readiness checks registered from configuration: `postgres` (single `PostgresOptions:ConnectionString`) or
`postgres-<module>` (per-module `PostgresOptions:ConnectionString:<Module>`), `postgres-<module>-persist-message`
(`PersistMessageOptions:ConnectionString:<Module>`), `mongodb`, `eventstore`, `rabbitmq`
(`MessageBroker:TransportType=RabbitMq`), plus checks other components add (Booking's `grpc-flight` /
`grpc-passenger`, MassTransit's bus check). `HealthOptions:DependencyChecksEnabled=false` turns the dependency
checks off; `HealthOptions:Enabled=true` adds the HealthChecks UI at `/health-ui`. The gRPC health service
(`grpc.health.v1`) on the h2 port reports the same checks.

Compose example:

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -fsS http://localhost/health/ready || exit 1"]
```

Kubernetes: `livenessProbe.httpGet.path: /health/live`, `readinessProbe.httpGet.path: /health/ready`.
