# Booking service

Booking is the standalone host for the Booking module; the monolith host is retired. Start it locally with:

```sh
dotnet run --project src/Services/Booking.Host/Booking.Host.csproj
```

The launch profile listens on `http://localhost:5104`. Build and run the container with:

```sh
docker build -f src/Services/Booking.Host/Dockerfile .
docker run -p 8080:8080 booking-host
```

The container listens on port `8080` and sets `ASPNETCORE_ENVIRONMENT=docker` to load
`appsettings.docker.json`. Its default configuration uses the Docker service names
`postgres`, `mongo`, `eventstore`, `rabbitmq`, `flight-service`, and `passenger-service`.

## Configuration

- `AppOptions:Name`: root endpoint response (`Booking-Service`).
- `PersistMessageOptions:Interval` and
  `PersistMessageOptions:ConnectionString:Booking`: outbox processing interval and
  PostgreSQL connection.
- `MongoOptions:ConnectionString` and
  `MongoOptions:Booking:DatabaseName`: MongoDB connection and read database.
- `EventStoreOptions:ConnectionString`: EventStoreDB connection.
- `Jwt:Authority` and `Jwt:Audience`: public issuer and token audience. By default the authority
  is the Gateway at `http://localhost:5000`.
- `Jwt:MetadataAddress`: internal Identity discovery document (`http://localhost:5101` locally,
  `http://identity-service:8080` in Docker). Configure Identity.Host's `AuthOptions:IssuerUri` as
  `http://localhost:5000` when it issues tokens through the Gateway.
- `Grpc:FlightAddress` and `Grpc:PassengerAddress`: upstream gRPC services.
- `Messaging:Transport` and `RabbitMqOptions`: message transport and RabbitMQ
  connection.
- `Modules:Booking:BackgroundProcessingEnabled`: enables Booking background
  consumers and the EventStoreDB subscription; defaults to `true`.
- `ObservabilityOptions:InstrumentationName` and
  `ObservabilityOptions:ServiceName`: OpenTelemetry identity (`booking_service` /
  `booking-service`).

`/health/live` is the liveness endpoint. `/health/ready` checks PostgreSQL, MongoDB,
EventStoreDB HTTP liveness, RabbitMQ when selected, the Booking gRPC dependencies, and
the active EventStoreDB `$all` subscription when Booking background processing is enabled.
Both endpoints are available in every environment.

## Background-consumer cut-over

The `$all` EventStoreDB subscription uses checkpoint stream `checkpoint_default`,
and the Booking Mongo projection performs a check-then-insert. Only one process
(the monolith or this host) may run with Booking background processing enabled
against the same EventStoreDB and MongoDB. While this host is active, set
`Modules__Booking__BackgroundProcessingEnabled=false` on the monolith. This
separation is mandatory during [previous-release recovery](../../../docs/runbooks/cutover/decommission.md);
the default topology runs only this host.
