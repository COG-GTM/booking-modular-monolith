# Booking Modular Monolith integration-event contracts

This package contains the integration-event records and the `IEvent` /
`IIntegrationEvent` abstractions they implement. It preserves the existing CLR namespaces and
type names used by MassTransit message URNs.

## Compatibility policy

Contracts are additive: add a new event or an optional property at the end of an event. Additive
changes receive a minor version bump. For a breaking change, add a new `<Name>V2` type in the same
namespace and publish it alongside the old type until consumers move; retire the old type only in
a later major release. Never rename or move a contract type or namespace, because that changes
its MassTransit URN. See [ADR 0005](../../../docs/adr/0005-contract-and-proto-versioning-policy.md)
for the full policy.

## Versioning and local consumption

Set the package version in `Contracts.Messages.csproj` for a release. Use a minor bump for
additive changes and a major bump for a breaking release after the V2 migration. To pack and
consume version `1.0.0` from the repository-local feed:

```sh
bash scripts/pack-buildingblocks.sh 1.0.0
dotnet restore MyService.csproj --source ./artifacts/packages --source https://api.nuget.org/v3/index.json
```

For package-mode checks of this package and its consumers, run:

```sh
bash scripts/test-contracts-package.sh 1.0.0
```

Normal repository builds use the project reference by default. Pass
`-p:UseContractsPackage=true -p:ContractsMessagesVersion=1.0.0` to consume the pinned package.
