# Cross-service end-to-end tests

These tests launch the Identity, Flight, Passenger, Booking, and Gateway assemblies as real
`dotnet` processes. PostgreSQL, MongoDB, RabbitMQ, and EventStoreDB run in Testcontainers; Docker
must be available to the test process. The fixture starts services in dependency order and checks
their readiness endpoints before running the flows.

Run the suite from the repository root:

```sh
dotnet test tests/CrossService.EndToEnd.Test
```

The test collection is sequential and includes a registration/message-broker flow, a Booking-to-
Flight/Passenger gRPC flow, and an unauthenticated gateway check. The booking flow makes one
booking against the seeded flight. A pre-existing `Guid.Empty` aggregate-ID issue means a second
booking in the same fixture is not supported.

By default, the fixture uses `postgres:16`, `mongo:latest`, `rabbitmq:management`, and
`eventstore/eventstore:latest`. Override any image with `E2E_POSTGRES_IMAGE`, `E2E_MONGO_IMAGE`,
`E2E_RABBITMQ_IMAGE`, or `E2E_EVENTSTORE_IMAGE`. Each service receives its own PostgreSQL role and
database from `deployments/docker-compose/init/postgres/01-service-databases.sql`; the Mongo,
RabbitMQ, EventStoreDB, endpoint, gRPC, and authentication settings are supplied through process
environment variables.

Service stdout/stderr is written to
`TestResults/e2e-logs/{identity,flight,passenger,booking,gateway}.log` under the test output
directory. The suite usually takes a few minutes, depending on image availability and container
startup time.
