# 0495. Per-service observability, health probes and distributed trace propagation

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Authors:** Devin (AB-495)
- **Owning team:** TBD — owner to confirm before ARB
- **Related ADRs:** [0003](0003-inter-service-communication.md),
  [0007](0007-unauthenticated-internal-grpc-accepted-risk.md),
  [0008](0008-service-host-layout-and-ports.md)

## Context

OpenTelemetry, health checks and Serilog lived in `BuildingBlocks` and Aspire `ServiceDefaults`
but were wired for one host: probes were only mapped in Development (or ad hoc per host), every
host reported a free-text `service.name`, readiness had no dependency checks, and messages written
to the outbox lost the request's trace context because they are published later by a background
worker. With the gateway plus four service hosts (AB-488), one request now crosses five processes,
gRPC and RabbitMQ.

ARB triage: `ARB_LIGHT`. The change modifies existing shared platform components
(`BuildingBlocks.HealthCheck`, `BuildingBlocks.OpenTelemetryCollector`,
`BuildingBlocks.PersistMessageProcessor`, ServiceDefaults) and exports to the already-provisioned
OTel collector / Jaeger / Tempo / Prometheus / Grafana / Loki stack. No new deployable, data store,
vendor, contract or auth boundary is introduced. The detector's T3 hit is a false positive (an
`esdb+discover://` URL in unit-test data).

## Decision

1. **Service identity.** `service.name` resolves from `OTEL_SERVICE_NAME`, then
   `ObservabilityOptions:ServiceName`, then the application name. Canonical names: `gateway`,
   `identity-service`, `flight-service`, `passenger-service`, `booking-service`.
   `OTEL_EXPORTER_OTLP_ENDPOINT` overrides the configured OTLP endpoint.
2. **Probes in every environment.** `UseServiceDefaults()` maps `/health/live` (only the `self`
   check, tag `live`) and `/health/ready` (all checks) anonymously, plus the legacy `/health` and
   `/alive`. Readiness registers checks from configuration: Postgres (per module, plus
   persist-message), MongoDB, EventStoreDB (HTTP liveness of the `esdb://` node), RabbitMQ when
   `MessageBroker:TransportType=RabbitMq`, and host-registered checks such as the Booking gRPC
   client health checks. `HealthOptions:DependencyChecksEnabled` opts out; `HealthOptions:Enabled`
   now only toggles the HealthChecks UI.
3. **Trace propagation.** W3C `traceparent`/`tracestate` are used everywhere: ASP.NET Core,
   HttpClient (YARP forwarding) and gRPC client instrumentation for sync hops; MassTransit's
   `MassTransit` diagnostic source for broker hops; and the outbox stores the `traceparent` of the
   writing request in the persisted envelope, then starts an `outbox publish <type>` span (source
   `BuildingBlocks.PersistMessageProcessor`) parented to it before handing the message to
   MassTransit. `Yarp.ReverseProxy` spans are collected on the gateway.
4. **Metrics/dashboards.** Prometheus scrapes `/metrics` on each service (`services` job) and a
   provisioned Grafana dashboard (`booking-services.json`) is filtered by `service_name`.

## Alternatives considered

- **Per-host probe mappings** (status quo): duplicated, diverging paths and predicates; rejected.
- **Link outbox publish spans instead of parenting them**: keeps request traces short, but a
  CreateBooking/RegisterUser flow would no longer be one trace; rejected for now.
- **Expose probes through the gateway**: probes are consumed by compose/Kubernetes directly on each
  container; routing them through the gateway would couple orchestration to the edge.

## Consequences

- Anonymous `/health/ready` returns dependency names and status in production. Service ports are
  internal (only the gateway is published), and the gateway's own readiness contains only `self`.
  If services are ever exposed directly, restrict or trim the readiness payload.
- Long outbox delays stretch the originating trace's duration; spans remain correct.
- Hosts must not map their own `/health/live`/`/health/ready` (ambiguous route matches).
