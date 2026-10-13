# 0006. Versioning and compatibility policy for contracts and protos

- **Status:** Proposed
- **Date:** 2026-10-13
- **ARB ticket:** TO BE CREATED
- **Related ADRs:** [0003](0003-inter-service-communication.md); implemented by AB-506 (event contracts),
  AB-494 (protos), AB-500 (package feed), AB-504 (contract tests)

## Context

Once services deploy independently, two kinds of contracts become cross-service APIs:

- **Integration events** in `src/BuildingBlocks/Contracts/EventBus.Messages`
  (`UserCreated`, `PassengerCreated`, `FlightCreated`, `SeatReserved`, `BookingCreated`, ...);
- **gRPC protos**, today duplicated between the Booking client
  (`src/Modules/Booking/src/GrpcClient/Protos`) and the Flight/Passenger servers
  (`src/Modules/*/src/GrpcServer/Protos`) - with mismatched `package` names.

Feature slices already version their HTTP endpoints (`V1` folders, `/api/v1.0/...`). Independent
deploys need explicit rules so a producer never breaks a consumer that has not been redeployed.

## Decision

**Single source of truth, packaged**

- Integration-event contracts move to `src/Contracts/Contracts.Messages` (AB-506).
- gRPC protos move to `src/Contracts/Contracts.Grpc` (AB-494); servers and clients generate code
  from the same `.proto` files. Services must not copy protos or event types.
- Shared libraries (contracts and the split BuildingBlocks, AB-500) are versioned with **SemVer** and
  packed to the local NuGet feed **`./artifacts/packages`** (configured in `nuget.config` by
  AB-500). In-repo builds may use project references; the package version is what an external or
  independently released service pins.

**Compatibility rules**

- **Additive-only** within a version: add optional properties/fields, add new messages, add new
  RPCs. Consumers are tolerant readers (ignore unknown fields).
- Protos: never renumber or reuse a field tag; removed fields become `reserved`; never change a
  field's type or meaning.
- A **breaking change = a new versioned message/proto**, following the existing `V1` convention:
  e.g. `UserCreatedV2` published alongside `UserCreated`, or a new proto package/service
  (`flight.v2`) served side by side. The old version is kept until every consumer has migrated,
  then removed in a later major release.
- SemVer for packages: patch = no contract change, minor = additive change, major = removal of a
  deprecated version.
- Contract tests (AB-504) keep baselines of event schemas and proto descriptors and fail CI on a
  non-additive change.

## Alternatives considered

| Alternative | Pros | Cons | Why rejected |
| --- | --- | --- | --- |
| Keep copies per service | No shared package | Drift (already happened: package-name mismatch) | Proven failure mode |
| Schema registry (Avro/Protobuf registry) | Central enforcement | New infra/vendor | Overkill for 4 services; contract tests suffice |
| Version everything in lock-step | Simple mental model | Forces coordinated deploys | Defeats independent deployability |

## Consequences

- Services can deploy in any order as long as rules are followed.
- Deprecated contract versions carry a maintenance cost and need explicit removal.
- Contract changes are reviewed at one place (the contracts projects).
