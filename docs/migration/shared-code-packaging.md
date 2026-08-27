# Shared Code Packaging

The migration now keeps these contracts standalone:

- `EventBus.Messages` contains integration-event contracts.
- `Contracts.Grpc` contains the canonical Flight and Passenger gRPC contracts.

Both projects are consumed through `ProjectReference`, not NuGet packages.
The solution remains unified while services are extracted, so NuGet versioning
would add friction without providing isolation. Revisit this decision when
services and shared-code ownership move into separate repositories or require
independent release/versioning.

Per-service hosts may reference `BuildingBlocks`, `EventBus.Messages`, and
`Contracts.Grpc`. They must never reference another module.
