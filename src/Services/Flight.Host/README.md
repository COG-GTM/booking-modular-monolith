# Flight service host

`Flight.Host` runs only the Flight module. It calls the module's existing `AddFlightModules` and
`UseFlightModules` extensions; database contexts and persistence workers are registered by the
module, not duplicated by the host.

## Run locally

Start PostgreSQL 16 with the service database initialization script and the required prepared
transaction capacity, plus MongoDB, RabbitMQ, and EventStoreDB for the monolith used to issue
development tokens:

```sh
docker run -d --name flight-postgres -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres \
  -v "$PWD/deployments/docker-compose/init/postgres/01-service-databases.sql:/docker-entrypoint-initdb.d/01-service-databases.sql:ro" \
  postgres:16 -c max_prepared_transactions=100
docker run -d --name flight-mongo -p 27017:27017 mongo
docker run -d --name flight-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management
docker run -d --name flight-eventstore -p 2113:2113 -p 1113:1113 \
  eventstore/eventstore:latest
```

Run the host from the repository root:

```sh
SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs" \
  dotnet run --project src/Services/Flight.Host/Flight.Host.csproj
```

The REST/Kestrel HTTP1 endpoint is `http://localhost:5102`; the separate gRPC h2c endpoint is
`http://localhost:5112`. Docker listens on HTTP `8080` and gRPC `8081`. When using the monolith
to issue development tokens, run it on `http://localhost:3001` with
`Modules__Flight__BackgroundProcessingEnabled=false`. The default JWT settings expect tokens with
issuer `http://localhost:5000` and discovery metadata from Identity.Host on port `5101`; override
`Jwt__Authority` and `Jwt__MetadataAddress` when using another issuer.

## Configuration

ASP.NET Core environment variables use the double-underscore form shown below and override JSON.
For example, `PostgresOptions__ConnectionString__Flight` corresponds to
`PostgresOptions:ConnectionString:Flight`.

| Configuration key (environment-variable form) | Purpose |
| --- | --- |
| `AppOptions:Name` (`AppOptions__Name`) | Name returned by `GET /`. |
| `Kestrel:Endpoints:Http:Url` / `Protocols` (`Kestrel__Endpoints__Http__Url` / `Kestrel__Endpoints__Http__Protocols`) | REST address and HTTP protocols. |
| `Kestrel:Endpoints:Grpc:Url` / `Protocols` (`Kestrel__Endpoints__Grpc__Url` / `Kestrel__Endpoints__Grpc__Protocols`) | Separate gRPC address; h2c requires HTTP/2 on its own port. |
| `PostgresOptions:ConnectionString:Flight` (`PostgresOptions__ConnectionString__Flight`) | Flight write database. |
| `PersistMessageOptions:Interval` / `Enabled` (`PersistMessageOptions__Interval` / `PersistMessageOptions__Enabled`) | Outbox polling interval and processing switch. |
| `PersistMessageOptions:ConnectionString:Flight` (`PersistMessageOptions__ConnectionString__Flight`) | Flight outbox database. |
| `MongoOptions:ConnectionString` (`MongoOptions__ConnectionString`) | Shared Mongo connection fallback. |
| `MongoOptions:Flight:ConnectionString` / `DatabaseName` (`MongoOptions__Flight__ConnectionString` / `MongoOptions__Flight__DatabaseName`) | Named Flight read-store connection and database. |
| `RabbitMqOptions:HostName` / `Port` / `VirtualHost` / `UserName` / `Password` (`RabbitMqOptions__HostName` / `RabbitMqOptions__Port` / `RabbitMqOptions__VirtualHost` / `RabbitMqOptions__UserName` / `RabbitMqOptions__Password`) | RabbitMQ fallback settings. |
| `ConnectionStrings:rabbitmq` (`ConnectionStrings__rabbitmq`) | Optional AMQP URI, preferred over `RabbitMqOptions` by MassTransit and readiness. |
| `Messaging:Transport` / `Messaging:AllowInMemory` (`Messaging__Transport` / `Messaging__AllowInMemory`) | Select MassTransit transport; InMemory disables the RabbitMQ readiness check. |
| `Jwt:Authority` / `Audience` / `MetadataAddress` (`Jwt__Authority` / `Jwt__Audience` / `Jwt__MetadataAddress`) | Token issuer, audience, and optional discovery-document address. The shared JWT extension disables HTTPS metadata requirements. |
| `ObservabilityOptions:InstrumentationName` / `ServiceName` (`ObservabilityOptions__InstrumentationName` / `ObservabilityOptions__ServiceName`) | OpenTelemetry instrumentation and service resource names. |
| `ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint` (`ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint`) | OTLP collector endpoint. |
| `ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpcExporterEndpoint` (`ObservabilityOptions__AspireDashboardOTLPOptions__OTLPGrpcExporterEndpoint`) | Aspire dashboard OTLP endpoint. |
| `ObservabilityOptions:ZipkinOptions:HttpExporterEndpoint` (`ObservabilityOptions__ZipkinOptions__HttpExporterEndpoint`) | Optional Zipkin exporter endpoint. |
| `ObservabilityOptions:JaegerOptions:OTLPGrpcExporterEndpoint` / `HttpExporterEndpoint` (`ObservabilityOptions__JaegerOptions__OTLPGrpcExporterEndpoint` / `ObservabilityOptions__JaegerOptions__HttpExporterEndpoint`) | Optional Jaeger exporter endpoints. |
| `ObservabilityOptions:UsePrometheusExporter` / `UseOTLPExporter` / `UseAspireOTLPExporter` / `UseGrafanaExporter` (`ObservabilityOptions__UsePrometheusExporter` / `ObservabilityOptions__UseOTLPExporter` / `ObservabilityOptions__UseAspireOTLPExporter` / `ObservabilityOptions__UseGrafanaExporter`) | Enable or disable exporters. |
| `Modules:Flight:BackgroundProcessingEnabled` (`Modules__Flight__BackgroundProcessingEnabled`) | Enable the Flight outbox polling worker. |
| `Logging:LogLevel:Default` / `Logging:LogLevel:Microsoft.AspNetCore` / `AllowedHosts` (`Logging__LogLevel__Default` / `Logging__LogLevel__Microsoft.AspNetCore` / `AllowedHosts`) | Standard ASP.NET Core logging and host filtering settings. |

`appsettings.docker.json` selects container DNS names (`postgres`, `mongo`, `rabbitmq`,
`identity-service`, and the telemetry services), exposes HTTP on `8080` and gRPC on `8081`, and
uses the `flight_service` PostgreSQL role rather than the PostgreSQL superuser.

## Health and routing

- `GET /health/live` includes only the `self` liveness check.
- `GET /health/ready` includes `self`, `postgres-flight`,
  `postgres-flight-persist-message`, `mongo-flight`, and `rabbitmq` (the RabbitMQ check is omitted
  for the InMemory transport).
- gRPC health check service `""` is liveness; `flight.FlightGrpcService` is readiness.
- The gateway can route Flight REST traffic here with
  `ReverseProxy__Clusters__flight__Destinations__primary__Address=http://localhost:5102/`.
- OpenTelemetry identifies this host as `flight-service` with instrumentation name
  `flight_service`.

## Double-consumer and double-poller risk

The monolith configuration in `src/Api/src/appsettings.json` points Flight at the same
`flight_service` write and persist-message databases. When the monolith and this host run
simultaneously, set `Modules__Flight__BackgroundProcessingEnabled=false` on the monolith (or
configure it to use different Flight databases) to avoid two outbox pollers and duplicate
consumers. The service cut-over is tracked by AB-505.

Flight integration events do not have subscribers yet. MassTransit publishes them with
`Mandatory=true`, so unroutable events remain pending in the outbox and generate an error log on
each poll until a consumer queue is bound.
