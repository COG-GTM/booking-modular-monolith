# Messaging runbook (RabbitMQ / MassTransit)

See [ADR 0101](../adr/0101-rabbitmq-transport-and-module-queues.md).

## Configuration

| Key (env form) | Default | Notes |
| --- | --- | --- |
| `MessagingOptions__Transport` | `RabbitMq` | `InMemory` only for unit tests / broker-less local runs |
| `ConnectionStrings__rabbitmq` | - | amqp(s) URI; set by Aspire; wins over `RabbitMqOptions` |
| `RabbitMqOptions__HostName` | `localhost` | `rabbitmq` inside compose |
| `RabbitMqOptions__Port` | `5672` | |
| `RabbitMqOptions__VirtualHost` | `/` | |
| `RabbitMqOptions__UserName` / `__Password` | `booking` / `booking` | local-dev defaults; inject secrets elsewhere |

Service hosts register their own module's consumers only:

```csharp
builder.Services.AddCustomMassTransit(
    builder.Configuration,
    builder.Environment,
    configureBus: null,
    typeof(PassengerRoot).Assembly);
```

Passenger's consumer definition attaches its module-owned inbox filter. For consumers without a definition, a
single-module host can attach its module-bound filter with
`bus.UsePersistMessageInbox(context, typeof(MyModuleInboxFilter<>))`; do not attach both.

The gateway does not consume or publish messages and needs no broker settings.

## Topology

- Exchanges: one fanout exchange per message type, named by its URN (e.g. `BuildingBlocks.Contracts.EventBus.Messages:UserCreated`).
- Queues: `<module>.<consumer>`, e.g. `passenger.register-new-user-handler`; failures after retries land in
  `<queue>_error`, MassTransit `Fault<T>` is published.
- Monolith and a service host for the same module share the queue. `Modules:<Module>:BackgroundProcessingEnabled=false`
  stops only that module's polling worker, not its consumers; stop the old host or remove its consumer registration
  when handing consumption over. Run one replica with background
  processing per service (outbox has no row locking).

## Local run

```bash
docker compose -f deployments/docker-compose/docker-compose.infrastructure.yaml up -d rabbitmq postgres
dotnet run --project src/Api/src
# management UI: http://localhost:15672 (booking/booking)
curl -s -u booking:booking http://localhost:15672/api/queues/%2F/passenger.register-new-user-handler
```

## Operations

- **Messages stuck in `persist_message` with status `InProgress` (outbox)**: broker unreachable - check
  `RabbitMqOptions`/connection string and broker health; the background worker re-publishes on its next poll.
- **Messages in `<queue>_error`**: inspect in the management UI (headers `MT-Fault-Message`, `MT-Fault-StackTrace`),
  fix the cause, then move them back to `<queue>` (shovel or "Move messages"). Redelivery is safe: the inbox skips
  messages already processed by that queue.
- **Duplicate side effects**: the inbox row id is derived from (queue, MessageId); check `persist_message` rows with
  `DeliveryType = Inbox` for that id.
- **Old queues** (`register-new-user`) left on a long-lived broker from before AB-501 can be deleted.

## Failure modes

- **Broker or database down**: the outbox worker logs `PersistMessage processing failed; retrying on the next poll`
  and keeps polling; pending outbox rows publish once RabbitMQ is back. The host does not stop.
- **Inbox rows left `InProgress`**: a consumer that exhausted its retries leaves its inbox row `InProgress` for
  inspection; the background poll ignores inbox rows, and moving the message back from `<queue>_error` reuses the row.
- **Concurrent duplicates** (same MessageId on two consumers at once): the second inbox insert loses on the primary
  key and that copy is acknowledged without running the consumer; the winning delivery is retried by the broker if it fails.
