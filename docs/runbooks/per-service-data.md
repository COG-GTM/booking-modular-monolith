# Module-owned persistence

Architecture reference: ADR 0003, **Service-owned data and background processing**,
reserved by the target-architecture change. This slice adds no ADR.

Each module registers its own outbox/inbox using
`builder.AddPersistMessageProcessor<TModule>("Module")`. The marker is the module's
root type. Consumers and command handlers use `IEventDispatcher<TModule>`,
`IPersistMessageProcessor<TModule>`, and `IPersistMessageDbContext<TModule>`.
The untyped contracts remain base contracts only; they are not registered in DI.
Booking's outbox registration belongs to the API composition root so Booking
continues to have no EF Core dependency.

## PostgreSQL and workers

Connection strings are evaluated when the context is resolved, in this order:

1. `ConnectionStrings:<module-kebab>-persist-message`
2. `PersistMessageOptions:ConnectionString:<Module>`
3. `ConnectionStrings:persist-message`
4. `PersistMessageOptions:ConnectionString` (existing scalar setting)

The last two options preserve the existing shared `persist_message` database.
No existing appsettings, Compose credentials or Aspire settings change. Separate
stores and credentials are opt-in. Workers claim their resolved store at startup;
only one polling owner per host/port/database is allowed within the host, avoiding
competing module workers on the shared table even when credentials differ. Run
only one polling application replica per store (there is no distributed lease).
Outbox delivery remains at-least-once, with stable message IDs for inbox deduplication.

`PersistMessageOptions:Interval` controls the shared polling interval in seconds.
`PersistMessageOptions:Enabled=false` disables **all polling workers**, but keeps
storage and dispatch registration available so commands can continue enqueueing.
`Modules:<Module>:BackgroundProcessingEnabled=false` disables only that module's
worker registration. On a shared store, another enabled module still drains shared
work; isolation of background work requires separate stores. Both switches default
to true. Restart the host after changing connections or switches.

Passenger's `RegisterNewUserDefinition` attaches the Passenger inbox filter to its
consumer endpoint. Inbox identity combines the receiving endpoint and required
transport MessageId. A processed ID is skipped; a failed consumer
leaves its inbox row in progress for retry. The endpoint serializes consumption
to avoid competing duplicate deliveries within this monolith.

## MongoDB

Register read contexts with `builder.AddMongoDbContext<TContext>("Module")`.
Inject `IOptionsSnapshot<MongoOptions>` and select `Get("Module")`.
`MongoOptions:<Module>:DatabaseName` falls back to the existing shared
`MongoOptions:DatabaseName`. Connection resolution is:

1. `MongoOptions:<Module>:ConnectionString`
2. `ConnectionStrings:mongo`
3. `MongoOptions:ConnectionString`

Flight, Passenger and Booking default to the shared `booking_modular_monolith_read`.
Identity has no Mongo read context. Unqualified Mongo context/repository/unit of
work services are not registered; inject the module's concrete read context.
Separate database names do not provide Mongo user authorization.

## Migration and tests

`UseMigration<TContext>()` migrates that context and executes only data seeders
from its assembly.

TestBase provisions four databases in one PostgreSQL container, resets their
tables (including outbox/inbox) with Respawner, and resets all three Mongo read
databases. Module workers are stopped before reset and recreated per test.
The separate persist PostgreSQL container is no longer used by the fixture.
Use `PostgresContainerOptions__ImageName` instead.

## Local credential-isolation check

The opt-in Compose override mounts `deployments/docker-compose/init/postgres` into
`/docker-entrypoint-initdb.d`; default Compose does not. Initialization runs only on a fresh PostgreSQL
volume. Each `<module>_service` role owns both `<module>_modular_monolith` and
`<module>_service`. PUBLIC cannot CONNECT to these databases; the owner can.
The cluster administrator remains privileged. Passwords equal role names only
for local development; deployed environments must provision managed secrets.
Mongo separation is by database name, not Mongo user authorization in this ticket.

Start a fresh development cluster with
`docker compose -f deployments/docker-compose/docker-compose.yaml -f deployments/docker-compose/docker-compose.per-service-data.yaml up -d postgres mongo eventstore`.
Then opt into the example settings using
`DOTNET_ENVIRONMENT=PerServiceData dotnet run --no-launch-profile --project src/Api/src`.
The example uses each role's `<module>_service` database for its outbox/inbox.
For existing volumes, review and provision the SQL separately (it fails if roles
or databases already exist). Do not replace existing volumes to enable isolation.

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
# Expected with default Compose: 10
```

Run only one polling replica per store. With an isolated Passenger store,
disabling Passenger's worker does not
disable its bus consumer/inbox: Identity events still arrive, but Passenger's
queued internal/outbox work remains unprocessed until its worker starts again.

Existing shared-store messages/read data are not moved automatically (AB-505). Before
switching a running environment, drain the old outbox and plan migration or
rebuilding of each module's read models; do not drop the old stores while they
contain pending work.
