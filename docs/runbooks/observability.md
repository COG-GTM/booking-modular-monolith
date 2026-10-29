# Observability runbook (per-service telemetry, probes, distributed tracing)

See [ADR 0495](../adr/0495-per-service-observability-and-trace-propagation.md).

## Service identity

| Deployable | `service.name` | Local probe base |
| --- | --- | --- |
| Gateway (`src/Gateway`) | `booking-gateway` | http://localhost:5000 |
| Monolith (`src/Api`) | `booking-modular-monolith` | http://localhost:3001 |
| Identity.Host | `identity-service` | http://localhost:5103 |
| Flight.Host | `flight-service` | http://localhost:5107 (gRPC h2c 5106) |
| Passenger.Host | `passenger-service` | http://localhost:5102 (gRPC h2c 5202) |
| Booking.Host | `booking-service` | http://localhost:5004 |

Resolution order: `OTEL_SERVICE_NAME` > `ObservabilityOptions:ServiceName` > assembly name. All carry
`service.namespace=booking`. Collector endpoint: `OTEL_EXPORTER_OTLP_ENDPOINT` > `ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint`
(docker appsettings point at `http://otel-collector:4317` and turn the Aspire dashboard exporter off).

## Probe contract (every host, every environment)

| Path | Checks | Use for |
| --- | --- | --- |
| `/health/live`, `/alive` | `live`-tagged (`self`) | liveness |
| `/health/ready` | everything not tagged `live` (host DBs, Mongo, RabbitMQ, ESDB, gRPC peers) | readiness / startup, compose `service_healthy` |
| `/health` | all | humans |

Responses are JSON (`{"status":"Healthy","checks":{...}}`), 200 when healthy, 503 otherwise. Check `description`s are only returned in Development (they can name hosts/errors). gRPC `grpc.health.v1`
stays on the h2c port of Flight/Passenger/monolith.

Compose (runtime image has no curl):

```yaml
healthcheck:
  test: ["CMD", "bash", "-c", 'exec 3<>/dev/tcp/127.0.0.1/80 && printf "GET /health/ready HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3 && head -n1 <&3 | grep -q " 200 "']
```

Kubernetes:

```yaml
livenessProbe:  { httpGet: { path: /health/live,  port: 80 }, periodSeconds: 10 }
readinessProbe: { httpGet: { path: /health/ready, port: 80 }, periodSeconds: 10, failureThreshold: 3 }
startupProbe:   { httpGet: { path: /health/ready, port: 80 }, periodSeconds: 5,  failureThreshold: 30 }
```

## Distributed trace of a booking

`POST /api/v1/booking` through the gateway produces one trace:
`booking-gateway` (YARP) -> `booking-service` (ASP.NET Core, MediatR) -> gRPC `flight-service` (GetById, GetAvailableSeats, ReserveSeat)
and `passenger-service` (GetById) -> later `outbox publish <Event>` + MassTransit publish/consume spans over RabbitMQ.
Outbox rows store `traceparent`/`tracestate` in the envelope headers; the worker's span is parented to it.

Find it: Jaeger http://localhost:16686 (service `booking-gateway`, operation `POST api/{version}/booking/**`), Grafana Explore -> Tempo,
or `curl 'http://localhost:16686/api/traces?service=booking-gateway&limit=5'`.

## Metrics and logs

- Prometheus labels: `service_name` (collector `resource_to_telemetry_conversion`), so e.g.
  `sum by (service_name)(rate(http_server_request_duration_seconds_count[5m]))`.
- `booking_health_status{check}` (1 healthy / 0.5 degraded / 0 unhealthy), published every 30 s; disable with `HealthOptions__MetricsEnabled=false`.
- Loki: `{service_name="flight-service"}`.
- Grafana dashboard **Booking services - per-service overview** (`uid booking-services`, `deployments/configs/grafana/dashboards/booking-services.json`).

## Troubleshooting

- No spans from a docker host: check `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTLPGrpcExporterEndpoint` resolves to the collector, not localhost.
- Outbox publish in a separate trace: row was written outside a traced request (seed/migration) or before this change.
- Readiness 503: the JSON body names the failing check; liveness stays 200 so the orchestrator will not restart the pod for a dependency outage.
