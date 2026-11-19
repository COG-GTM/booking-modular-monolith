# Integration-event contracts (`Contracts.Messages`)

`src/Contracts/Contracts.Messages` holds every integration event exchanged between modules/services
(`BuildingBlocks.Contracts.EventBus.Messages.*`) plus the `IEvent` / `IIntegrationEvent` marker interfaces.
It is packed as **`BookingModularMonolith.Contracts.Messages`** with its own SemVer version, independent of
BuildingBlocks. Decision record: [ADR 0506](../../docs/adr/0506-extract-message-contracts.md).

The assembly depends only on `MassTransit.Abstractions` and `MediatR.Contracts`: no module code, no ASP.NET Core,
no EF Core / Mongo / EventStoreDB / RabbitMQ. `tests/Contracts.Messages.Tests` fails the build if that changes.

| Message (namespace `BuildingBlocks.Contracts.EventBus.Messages`) | Producer | Consumers |
|---|---|---|
| `FlightCreated`, `FlightUpdated`, `FlightDeleted`, `AircraftCreated`, `AirportCreated`, `SeatCreated`, `SeatReserved` | Flight | - |
| `UserCreated` | Identity | Passenger (`RegisterNewUserHandler`) |
| `PassengerRegistrationCompleted`, `PassengerCreated` | Passenger | - |
| `BookingCreated` | Booking | - |

## Referencing the contracts

Producers and consumers import `Contracts.Messages.Reference.props` instead of referencing the project:

```xml
<Import Project="..\..\..\Contracts\Contracts.Messages.Reference.props" />
```

- Default (in-repo development, CI, Docker): a `ProjectReference` to `Contracts.Messages.csproj`.
- `-p:UseContractsPackage=true`: an exact `PackageReference` to `BookingModularMonolith.Contracts.Messages`
  at the version declared in `Versions.props`. To override it, pass `-p:ContractsMessagesPackageVersion=<version>`
  on the build or restore command so the same pin reaches the module and its BuildingBlocks project references.
  Setting the property only in a module's project file does not propagate to those references.
- An independent service can declare a direct `PackageReference` with an exact version, for example `Version="[1.0.0]"`.

```bash
scripts/pack-contracts.sh [semver]              # -> artifacts/nuget (git-ignored local feed)
scripts/verify-contracts-consumer.sh [semver]   # standalone consumer + four modules built in package mode
```

`scripts/pack-building-blocks.sh` packs the contracts first, because `BookingModularMonolith.BuildingBlocks.Core`
depends on the contracts package.

## Compatibility policy

The CLR full name of a message is its wire identity: MassTransit derives the message URN from it
(`urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated`) and the outbox/inbox store it as `DataType`.
Producers and consumers will be deployed independently, so for every published message:

1. **Additive changes only** (MINOR bump): new messages, or new members appended to an existing record, which
   older consumers ignore and newer consumers must treat as optional.
2. **Never** rename, remove or retype a member, rename/move a message, or change its namespace.
3. **Breaking change = new message version.** Add a new record (e.g. `UserCreatedV2`) next to the old one, as the
   feature folders already do with `V1`; producers publish both until every consumer has moved to the new one, then
   the old one is deprecated (`[Obsolete]`) and removed only in a MAJOR release.
4. PATCH bumps are for docs/metadata only.

Bump `ContractsMessagesVersion` in [`Versions.props`](Versions.props) with every published change.

`MessageCompatibilityTests` compares the current contracts with the committed
[`tests/Contracts.Messages.Tests/Baselines/messages.json`](tests/Contracts.Messages.Tests/Baselines/messages.json):
removed/retyped members, removed messages or changed URNs fail; an additive change fails only until the baseline is
regenerated, so it is reviewed explicitly:

```bash
UPDATE_CONTRACT_BASELINES=true dotnet test src/Contracts/tests/Contracts.Messages.Tests
```

## Moved types

`BuildingBlocks.Core` forwards its former event interfaces and `BuildingBlocks.Contracts` forwards its
former message types. Code compiled against those assemblies can still resolve them; new consumers
reference `Contracts.Messages` directly.
