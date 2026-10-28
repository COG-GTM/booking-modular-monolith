# RabbitMQ messaging

## Configuration and host integration

`AddCustomMassTransit(environment, configuration, serviceName, consumerAssemblies)` selects
RabbitMQ by default. Pass only the current service's module assembly; a publisher-only host
passes no assemblies. Do not pass `AppDomain.CurrentDomain.GetAssemblies()`.
The gateway only proxies HTTP and does not need a bus or an inbox store.

```csharp
builder.Services.AddCustomMassTransit(
    builder.Environment, builder.Configuration, "passenger", typeof(PassengerRoot).Assembly);
```

Each host must register its own `IPersistMessageProcessor`/`IPersistMessageDbContext` store
before starting consumers (ADR 0004). This change does not change store registration.
Consumer queues are service-prefixed kebab-case names; the monolith's existing consumer is
`monolith-register-new-user-handler`, and the Passenger host's equivalent is
`passenger-register-new-user-handler`. Replicas of the same service share the same name.
Only one of those endpoints should own the production Passenger write path during cutover.

| Configuration | Default / purpose |
| --- | --- |
| `Messaging__Transport` | `RabbitMq`; `InMemory` by default only in environment `test` |
| `Messaging__AllowInMemory` | `false`; must also be `Development` to opt into `InMemory` |
| `ConnectionStrings__rabbitmq` | Optional AMQP URI, takes precedence over typed options; Aspire supplies this |
| `RabbitMqOptions__HostName` | `localhost`; `rabbitmq` from compose containers |
| `RabbitMqOptions__Port` | `5672` |
| `RabbitMqOptions__VirtualHost` | `/` |
| `RabbitMqOptions__UserName` / `Password` | Local-only `guest` defaults; inject credentials in production |
| `PersistMessageOptions__Interval` | Existing polling interval, 30 seconds |

Compose accepts `RABBITMQ_USERNAME` / `RABBITMQ_PASSWORD` for both broker and monolith.
Changing bootstrap credentials on an existing RabbitMQ volume does not rotate an existing
user; rotate through the broker's normal administration process. The named volume preserves
durable queues across container recreation. Production must use restricted credentials,
private ingress and TLS; the development compose stack is not a production security baseline.

## Delivery guarantees

- The outbox row ID becomes MassTransit's `MessageId` on every publish, including replay.
- Outbox publication is mandatory: RabbitMQ must route to at least one queue. Without any
  subscriber topology (including first startup), the broker rejects the publication and the
  row stays pending. Start a consumer or provision its topology, then the worker retries.
  This is not an acknowledgement from every intended subscriber: provision all required
  endpoint bindings before enabling producers when adding another subscriber to a live system.
- A PostgreSQL `inbox_message` receipt is keyed by `(message_id, consumer)`, independent of
  the event body's `EventId`. The filter is registered for both transports. Consumers using
  the in-memory transport still need a registered persistence store if they consume messages;
  isolated handler unit tests can invoke the consumer directly or substitute the processor.
- A transaction-scoped advisory lock serializes concurrent copies at an endpoint. The receipt
  commits only after successful consumer completion. Exceptions roll back the receipt and
  clear failed tracked state before retry. Separate endpoints each receive their own copy.
- Handler effects in other databases or external systems are **at least once**, not an atomic
  distributed transaction. Handlers must remain idempotent (Passenger checks passport existence).
- Transient consumer exceptions retry three times with exponential backoff (200 ms–5 s).
  Validation exceptions skip retry. Exhausted messages go to `<endpoint>_error`; no success
  receipt is committed. Keep the broker `MessageId` when replaying after fixing the cause.
- If outbox publication fails, the row stays pending. The polling worker logs the error and
  continues other rows, then retries failed rows on its next interval. A single unroutable
  event cannot starve other deliveries; publication/status-update failures may cause safe redelivery.
  Events intentionally having no subscribers will remain pending; investigate them rather
  than marking them delivered. Local broker/management ports bind only to host loopback;
  containers connect over the compose network. Replace development credentials for deployment.

## Local verification

```bash
docker compose -f deployments/docker-compose/docker-compose.yaml up -d postgres mongo eventstore rabbitmq
dotnet run --project src/Api/src/Api.csproj
dotnet test src/BuildingBlocks/tests/Messaging.Integration.Test
```

Use the normal seeded Identity login to obtain a token and call the authenticated
`POST /api/v1/identity/register-user` with a unique username, email and passport (≤10 chars).
After the outbox interval, verify the Passenger row and the inbox receipt; the broker
management UI on port 15672 must show the `UserCreated` exchange and consumer queue.
The automated broker suite uses disposable RabbitMQ and PostgreSQL Testcontainers and
covers replay, retries, error queues, concurrent duplicates and endpoint/bus restarts.

## Operations / rollback

Monitor pending outbox age/count, queue depth, disconnected consumers, `_error` queues and
polling-worker delivery errors. Broker outages must not trigger a fallback to an in-memory
bus: restore the broker/credentials and allow pending outbox rows to drain. Inspect the cause
of an error message before replaying it; never delete receipts as a replay shortcut.

Rollback application changes with publishers and consumers coordinated on the same transport;
do not switch one service to in-memory while the others still use RabbitMQ. Preserve broker
volumes and pending rows. Owning team, retention and production HA/TLS: owner confirmation
required in ADR 0021 before ARB approval.
