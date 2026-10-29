# Module-owned persistence

Each module registers its own outbox/inbox using
`builder.AddPersistMessageProcessor<TModule>("Module")`. The marker is the module's
root type. Consumers and command handlers use `IEventDispatcher<TModule>`,
`IPersistMessageProcessor<TModule>`, and `IPersistMessageDbContext<TModule>`.
The untyped contracts remain base contracts only; they are not registered in DI.
The active composition root registers Booking's outbox processor: the API host
for monolith operation or `Booking.Host` for standalone operation. Booking itself
continues to have no EF Core dependency.

## PostgreSQL and workers

Connection strings are evaluated when the context is resolved, in this order:

1. `ConnectionStrings:<module-kebab>-persist-message`
2. `PersistMessageOptions:ConnectionString:<Module>`
3. `ConnectionStrings:<module-kebab>`
4. `PostgresOptions:ConnectionString:<Module>`

The last two options put `persist_message` in the module's own PostgreSQL database
(the default). There is no shared persist-message database or scalar connection
string. Provision each module database before starting the application. The
monolith uses `booking_modular_monolith`; the standalone Booking host uses the
separate `booking_service` database and role. The other monolith database names
are prefixed with their module names.

`PersistMessageOptions:Interval` controls the shared polling interval in seconds.
`PersistMessageOptions:Enabled=false` disables **all polling workers**, but keeps
storage and dispatch registration available so commands can continue enqueueing.
`Modules:<Module>:BackgroundProcessingEnabled=false` disables that module's
outbox worker and excludes its consumer assembly from that composition's
MassTransit registration. For Booking, it also disables the EventStoreDB
subscription. The switch defaults to true. Restart the host after changing it.
The standalone host uses `Modules:Booking:SubscriptionId=booking-service`; when
unset the module keeps the `default` checkpoint for monolith compatibility. A
distinct subscription starts with an independent checkpoint and can replay from
the beginning.

Passenger's `RegisterNewUserDefinition` attaches the Passenger inbox filter to its
consumer endpoint. Inbox identity uses the transport MessageId when present,
falling back to the event identity. A processed ID is skipped; a failed consumer
leaves its inbox row in progress for retry. The endpoint serializes consumption
to avoid competing duplicate deliveries within this monolith.

## MongoDB

Register read contexts with `builder.AddMongoDbContext<TContext>("Module")`.
Inject `IOptionsSnapshot<MongoOptions>` and select `Get("Module")`.
`MongoOptions:<Module>:DatabaseName` is required. Connection resolution is:

1. `MongoOptions:<Module>:ConnectionString`
2. `ConnectionStrings:mongo`
3. `MongoOptions:ConnectionString`

Flight, Passenger and monolith Booking default to `flight_modular_monolith_read`,
`passenger_modular_monolith_read`, and `booking_modular_monolith_read`.
Standalone Booking defaults to `booking_service_read`.
Identity has no Mongo read context. Unqualified Mongo context/repository/unit of
work services are not registered; inject the module's concrete read context.
Health checks probe each configured module database.

## Migration and tests

`UseMigration<TContext>()` migrates that context and executes only data seeders
from its assembly.

TestBase provisions four databases in one PostgreSQL container, resets their
tables (including outbox/inbox) with Respawner, and resets all three Mongo read
databases. Module workers are stopped before reset and recreated per test.
The separate persist PostgreSQL container and all `PostgresPersistContainerOptions`
overrides have been removed. Use `PostgresContainerOptions__ImageName` instead.

## Local credential-isolation check

Compose mounts `deployments/docker-compose/init/postgres` into
`/docker-entrypoint-initdb.d`. Initialization runs only on a fresh PostgreSQL
volume. Each `<module>_service` role owns both `<module>_modular_monolith` and
`<module>_service`. PUBLIC cannot CONNECT to these databases; the owner can.
The cluster administrator remains privileged. Passwords equal role names only
for local development; deployed environments must provision managed secrets.
Mongo separation is by database name, not Mongo user authorization in this ticket.

After starting the Compose PostgreSQL service, verify over TCP (not the trusted
local Unix socket):

```bash
docker exec -e PGPASSWORD=flight_service postgres psql -h 127.0.0.1 \
  -U flight_service -d flight_modular_monolith -c 'SELECT current_user, current_database();'
# Expected success: flight_service | flight_modular_monolith
docker exec -e PGPASSWORD=flight_service postgres psql -h 127.0.0.1 \
  -U flight_service -d identity_modular_monolith -c 'SELECT 1;'
# Expected nonzero exit: FATAL: permission denied for database "identity_modular_monolith"
docker exec postgres psql -U postgres -d postgres -c 'SHOW max_prepared_transactions;'
# Expected: 100
```

Run only one polling replica per module, and only one replica with Booking
background processing enabled. When the standalone Passenger host owns
processing, set
`Modules__Passenger__BackgroundProcessingEnabled=false` on the monolith so it
stops both the Passenger poller and its RabbitMQ consumer. The standalone host
then consumes the Passenger queue and processes its own inbox and outbox.
Passenger's queued internal/outbox work remains unprocessed while its worker is
disabled.

Set `Modules:Booking:BackgroundProcessingEnabled=false` on the monolith during
side-by-side operation so its Booking outbox worker and EventStoreDB subscription
do not compete with the standalone host. See the
[Booking service runbook](./booking-service.md).

Existing shared-store messages/read data are not moved automatically (AB-505). Before
switching a running environment, drain the old outbox and plan migration or
rebuilding of each module's read models; do not drop the old stores while they
contain pending work.
