# Passenger Service

`Passenger.Host` runs the Passenger module as a standalone ASP.NET Core service.
It does not register any of the other modular-monolith modules.
The architecture decision is documented in [ADR 0018](../../../docs/adr/0018-passenger-service-host.md).

## Run locally

From the repository root:

```sh
dotnet run --project src/Services/Passenger.Host
```

The REST and health endpoints listen on `http://localhost:5103`; h2c gRPC
listens on `http://localhost:5113`. Both Kestrel endpoint URLs can be overridden
with environment variables such as
`Kestrel__Endpoints__Http__Url=http://localhost:5203`.

The container listens on HTTP/1.1 + HTTP/2 at port `8080` and HTTP/2-only gRPC
at port `8081`. Build it from the repository root with:

```sh
docker build -f src/Services/Passenger.Host/Dockerfile -t booking-passenger .
```

## Configuration

| Key | Local default | Purpose |
| --- | --- | --- |
| `AppOptions:Name` | `Passenger Service` | Root endpoint and displayed service name |
| `Kestrel:Endpoints:Http:Url` | `http://localhost:5103` | REST/HTTP endpoint (`Http1AndHttp2`) |
| `Kestrel:Endpoints:Grpc:Url` | `http://localhost:5113` | h2c gRPC endpoint (`Http2`) |
| `PostgresOptions:ConnectionString:Passenger` | `passenger_service` database, `passenger_service` role | Passenger write store |
| `PersistMessageOptions:ConnectionString:Passenger` | `passenger_service_persist_message` database | Passenger outbox/inbox store |
| `PersistMessageOptions:Interval` | `30` | Outbox polling interval in seconds |
| `MongoOptions:Passenger:ConnectionString` | `mongodb://localhost:27017` | Passenger read-store MongoDB connection |
| `MongoOptions:Passenger:DatabaseName` | `passenger_service_read` | Passenger read database |
| `RabbitMqOptions:HostName` | `localhost` | RabbitMQ host |
| `RabbitMqOptions:Port` | `5672` | RabbitMQ port |
| `RabbitMqOptions:VirtualHost` | `/` | RabbitMQ virtual host |
| `RabbitMqOptions:UserName` / `Password` | `guest` / `guest` | RabbitMQ credentials |
| `Messaging:Transport` | `RabbitMq` | MassTransit transport |
| `Jwt:Authority` | `https://localhost:3000` | JWT issuer |
| `Jwt:Audience` | `booking-modular-monolith` | Required API scope/audience |
| `Jwt:MetadataAddress` | unset | Optional internal discovery-document URL override |
| `Modules:Passenger:BackgroundProcessingEnabled` | `true` | Enables/disables only the Passenger outbox worker |
| `ObservabilityOptions:InstrumentationName` | `passenger-service` | OpenTelemetry instrumentation |
| `ObservabilityOptions:ServiceName` | `passenger-service` | OpenTelemetry resource service name |
| `ObservabilityOptions:OTLPOptions:OTLPGrpExporterEndpoint` | `http://localhost:4317` | OTLP gRPC exporter |
| `ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpExporterEndpoint` | `http://localhost:4319` | Aspire dashboard exporter |
| `ObservabilityOptions:ZipkinOptions:HttpExporterEndpoint` | `http://localhost:9411/api/v2/spans` | Zipkin exporter |
| `ObservabilityOptions:JaegerOptions:OTLPGrpcExporterEndpoint` | `http://localhost:14317` | Jaeger OTLP exporter |
| `ObservabilityOptions:JaegerOptions:HttpExporterEndpoint` | `http://localhost:14268/api/traces` | Jaeger HTTP exporter |
| `ObservabilityOptions:UsePrometheusExporter` | `true` | Exposes Prometheus metrics |
| `ObservabilityOptions:UseOTLPExporter` | `true` | Enables OTLP export |
| `ObservabilityOptions:UseAspireOTLPExporter` | `true` | Enables Aspire OTLP export |
| `ObservabilityOptions:UseGrafanaExporter` | `false` | Enables Grafana export |

The Docker environment uses the `postgres`, `mongo`, and `rabbitmq` service
hosts, and uses the same `passenger_service` role. `Jwt:Authority` is
`http://localhost:5000` while the optional metadata override targets the
Identity service's internal discovery URL.

The UserCreated consumer listens on
`passenger-service-register-new-user-handler`. Inbox receipts are stored in
`inbox_message` in `passenger_service_persist_message`.

## Health and gateway

- `GET /health/live` reports liveness.
- `GET /health/ready` checks Passenger Postgres, persist-message Postgres,
  MongoDB, and RabbitMQ (RabbitMQ is omitted for the in-memory transport).
- gRPC health reports the empty service and
  `passenger.PassengerGrpcService` using the live checks.

Point the gateway's Passenger cluster to this host:

```sh
ReverseProxy__Clusters__passenger__Destinations__primary__Address=http://localhost:5103/
```

For example, with `grpcurl` installed:

```sh
grpcurl -plaintext \
  -import-path src/Contracts/Contracts.Grpc/Protos \
  -proto passenger.proto \
  -d '{"Id":"<passenger-id>"}' \
  localhost:5113 passenger.PassengerGrpcService/GetById
```

## Previous-release rollback warning

Do not run this host's consumer alongside the monolith's
`monolith-register-new-user-handler` consumer while both use the same
Passenger database. Both consumers receive `UserCreated` and can write the
same passenger row. The default topology has no monolith; during
[previous-release recovery](../../../docs/runbooks/cutover/decommission.md), isolate the legacy consumer
before accepting registration writes.
`Modules:Passenger:BackgroundProcessingEnabled` gates only the outbox worker,
not the RabbitMQ consumer.
