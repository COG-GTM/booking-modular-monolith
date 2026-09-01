# Contracts.Messages

Shared integration-event contracts for the booking modules, packaged as an independently versioned artifact so producers and consumers can reference a pinned version once services are split.

## Contents

- `BuildingBlocks.Core.Event.IEvent` / `IIntegrationEvent` — base event abstractions (only lightweight dependencies: `MassTransit.Abstractions`, `MediatR.Contracts`).
- `BuildingBlocks.Contracts.EventBus.Messages` — the integration-event messages (Flight, Identity, Passenger, Reservation).

The original namespaces are preserved so existing producers and consumers keep working without source changes.

## Versioning (SemVer)

- **Patch** (`1.0.x`): non-functional changes (docs, metadata).
- **Minor** (`1.x.0`): additive changes only — new messages or new optional fields.
- **Major**: never for message shape changes. Breaking changes to an existing message must instead be published as a new message version (e.g. `RegisterNewUserV2`), matching the existing `V1` convention. Old versions stay in the package until all consumers have migrated.

## Backward-compatibility policy

1. Never remove or rename an existing message or its properties.
2. Never change a property's type or meaning.
3. Additive changes (new messages, new optional properties with defaults) are allowed in minor releases.
4. For breaking changes, add a new message version side by side (V2, V3, ...) and migrate consumers before retiring the old one.

## Publishing to a local feed

```bash
dotnet pack src/Contracts.Messages/Contracts.Messages.csproj -c Release -o ./local-nuget-feed
dotnet nuget add source ./local-nuget-feed --name local-contracts
```

Consumers can then pin a version:

```xml
<PackageReference Include="Contracts.Messages" Version="1.0.0" />
```

Within this repository the solution still uses a `ProjectReference` so the monolith builds without a pre-pack step; external services should consume the NuGet package.
