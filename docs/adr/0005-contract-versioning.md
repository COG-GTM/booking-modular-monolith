# ADR 0005: Versioning and compatibility policy for contracts and protos

## Status

Accepted

## Context

Once services deploy independently, the gRPC protos (`flight.proto`, `passenger.proto`)
and the MassTransit integration-event contracts become cross-service APIs. Today the
protos are duplicated between the Booking client and the Flight/Passenger servers, and
event contracts live in module code. Independent deployment requires explicit
compatibility rules.

## Decision

**Publish contracts as versioned packages** (see AB-232 for events, AB-237 for protos)
with semantic versioning and a backward-compatibility-first policy:

### Protos (gRPC)

- Single source of truth per service in a shared contracts package; consumers must not
  copy protos.
- Only **backward-compatible** changes within a major version: add fields with new tag
  numbers, add RPCs, add messages. Never reuse or renumber tags; removed fields are
  `reserved`.
- Breaking changes require a new proto package version (e.g. `bookingFlight.v2`) served
  side by side; the old version is kept until all consumers have migrated.

### Integration events

- Events are versioned types in the shared contracts package; consumers tolerate unknown
  fields (tolerant reader).
- Additive changes (new optional properties) are non-breaking. Renames/removals/semantic
  changes require a new event type (e.g. `UserCreatedV2`) published alongside the old one
  during a deprecation window.

### General

- Contract packages use SemVer: patch/minor = compatible additions, major = breaking.
- Provider services support at least the current and previous major contract version
  (N-1 compatibility) so consumers and providers never need lock-step deploys.
- CI must fail on breaking proto changes within a major version (e.g. `buf breaking`)
  once the contract packages exist.

## Consequences

- Services can deploy in any order; no coordinated releases.
- Deprecated versions carry a maintenance cost and need an explicit removal process.
- Contract changes get reviewed at the package boundary, making breaking changes visible.
