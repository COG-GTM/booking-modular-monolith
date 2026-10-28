# Booking service

Booking can run independently from the modular monolith. Start it locally with:

```sh
dotnet run --project src/Services/Booking.Host/Booking.Host.csproj
```

The launch profile listens on `http://localhost:5104`. Build and run the container with:

```sh
docker build -f src/Services/Booking.Host/Dockerfile .
docker run -p 8080:8080 booking-host
```

The container listens on port `8080`. Its default configuration uses the Docker service
names `postgres`, `mongo`, `eventstore`, `rabbitmq`, `flight-service`, and
`passenger-service`.

## Configuration

- `AppOptions:Name`: root endpoint response (`Booking-Service`).
- `PersistMessageOptions:Interval` and
  `PersistMessageOptions:ConnectionString:Booking`: outbox processing interval and
  PostgreSQL connection.
- `MongoOptions:ConnectionString` and
  `MongoOptions:Booking:DatabaseName`: MongoDB connection and read database.
- `EventStoreOptions:ConnectionString`: EventStoreDB connection.
- `Jwt:Authority`, `Jwt:Audience`, and optional `Jwt:MetadataAddress`: token
  validation settings.
- `Grpc:FlightAddress` and `Grpc:PassengerAddress`: upstream gRPC services.
- `Messaging:Transport` and `RabbitMqOptions`: message transport and RabbitMQ
  connection.
- `Modules:Booking:BackgroundProcessingEnabled`: enables Booking background
  consumers and the EventStoreDB subscription; defaults to `true`.
- `ObservabilityOptions:InstrumentationName` and
  `ObservabilityOptions:ServiceName`: OpenTelemetry identity (`booking_service` /
  `booking-service`).

`/health/live` is the liveness endpoint and `/health/ready` checks PostgreSQL,
MongoDB, EventStoreDB, RabbitMQ when selected, and the Booking gRPC dependencies.
Both endpoints are available in every environment.

## Background-consumer cut-over

The `$all` EventStoreDB subscription uses checkpoint stream `checkpoint_default`,
and the Booking Mongo projection performs a check-then-insert. Only one process
(the monolith or this host) may run with Booking background processing enabled
against the same EventStoreDB and MongoDB. While this host is active, set
`Modules__Booking__BackgroundProcessingEnabled=false` on the monolith. This
separation is intended for the AB-505 cut-over.
