# Integration-event contracts: versioning and compatibility policy

Applies to `src/Contracts/Contracts.Messages` (NuGet package
`BookingModularMonolith.Contracts.Messages`). Every producer and consumer of an integration event
references this package (in-repo via `ProjectReference`; split services may pin a
`PackageReference` version from the local feed `artifacts/packages`).

## Rules

1. **Additive changes only** on a published message type:
   - allowed: adding a new message type; adding a new property that is optional for consumers
     (nullable or with a default value) at the end of the record.
   - not allowed: removing or renaming a property, changing a property's type or meaning,
     making an optional property required, renaming the type, or moving it to another namespace.
2. **Breaking changes ship as a new versioned type** next to the old one, following the existing
   `V1` convention used for features and endpoints: e.g. `UserCreatedV2`. The producer publishes
   both `UserCreated` and `UserCreatedV2` until every consumer has moved to `V2`; only then may
   the old type be marked `[Obsolete]`, and it is removed in the next major package version.
3. **Namespaces and type names are part of the wire contract.** MassTransit builds the message
   URN (`urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated`) and the RabbitMQ
   exchange name from them, and the persistent outbox resolves stored messages by full type name.
   That is why the types keep the `BuildingBlocks.Contracts.EventBus.Messages` namespace even
   though they no longer live in BuildingBlocks.
4. **No dependencies on module code or heavy infrastructure.** The package may only depend on
   lightweight abstractions (currently `MassTransit.Abstractions`, `MediatR.Contracts`).

## Package versioning (SemVer)

| Change                                                   | Version bump |
|----------------------------------------------------------|--------------|
| New message type, new optional property, docs            | minor        |
| Bug fix with no shape change (e.g. attribute metadata)   | patch        |
| Removing an obsolete type (after all consumers migrated) | major        |

Bump `<Version>` in `Contracts.Messages.csproj` in the same PR as the contract change, then pack:

```bash
dotnet pack src/Contracts/Contracts.Messages/Contracts.Messages.csproj -c Release -o artifacts/packages
```

## Review checklist for a contract PR

- [ ] Change is additive, or introduces a new `V`-suffixed type instead of editing an existing one.
- [ ] `<Version>` bumped per the table above.
- [ ] Producer and all known consumers still build (`dotnet build booking-modular-monolith.sln`).
