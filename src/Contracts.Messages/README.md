# Integration-event contracts

`BookingModularMonolith.Contracts.Messages` is independently versioned using SemVer.
It contains only message records and their event interfaces. Its runtime dependencies
are `MassTransit.Abstractions` and `MediatR.Contracts`; no module, database, ASP.NET,
or transport implementation is needed by consumers.

## Local feed and pinned consumers

From the repository root, with the .NET SDK from `global.json`:

```sh
bash scripts/pack-contracts.sh
dotnet restore booking-modular-monolith.sln
dotnet build booking-modular-monolith.sln --no-restore
dotnet test booking-modular-monolith.sln --no-build
```

The script publishes the package to the git-ignored `artifacts/nuget` local feed.
`nuget.config` maps this package ID exclusively to that feed, avoiding accidental
resolution from a public source. Build the package **before** restoring the solution;
CI and the API Dockerfile do this automatically.

`ContractsMessagesVersion` in `Directory.Build.props` is the release/pin source of
truth. All producers and consumers use an exact NuGet range (`[1.0.0]` initially),
not a project reference. Bump this property for a release, pack, then restore.
Never replace a published package under the same version: NuGet caches versions.
There is deliberately no implicit source-project fallback.

An external service adds this repository's `artifacts/nuget` directory as a NuGet
source (alongside nuget.org for the two abstraction dependencies), then references:

```xml
<PackageReference Include="BookingModularMonolith.Contracts.Messages" Version="[1.0.0]" />
```

## Compatibility policy

- **PATCH**: implementation, documentation or packaging fixes without API/wire changes.
- **MINOR**: additive, backward-compatible messages or optional properties only.
  New fields must have safe defaults when absent. Do not add required constructor
  parameters to an existing message.
- Never rename, remove, retype or change the meaning of an existing message/member.
  Keep existing full type names: MassTransit derives message URNs from them, and
  the outbox stores them as `data_type`. Existing unqualified records are V1.
- Breaking changes introduce a distinct message, e.g. `UserCreatedV2`, following
  the modules' V1/V2 convention. Run both versions during a migration, update
  consumers before producers, and retain V1 until all consumers and queued/outbox
  messages have drained. Removal requires an explicitly coordinated MAJOR release.
- Package versions and message-schema versions are separate. Keep assembly version
  `1.0.0.0` within the first package major; package/file versions may advance.
- The old namespaces are intentional. BuildingBlocks forwards all moved types so
  old assembly-qualified names continue resolving. Do not rename them for tidiness.

`Contracts.Messages.Tests` checks the V1 public shape, JSON payloads, MassTransit
URNs/topology and package isolation. Keep its V1 expectations immutable; add new
version tests instead of rewriting old expectations. Passenger integration tests
exercise registration from Identity through the outbox and broker to Passenger.
