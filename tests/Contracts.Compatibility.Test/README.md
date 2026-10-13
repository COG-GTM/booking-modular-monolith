# Contracts.Compatibility.Test

Guards the shared contracts (`src/Contracts/Contracts.Messages`, `src/Contracts/Contracts.Grpc`) against
changes that would break an independently deployed producer or consumer (ADR 0006). No Docker needed.

| Check | Files | Fails when |
|---|---|---|
| Message schema | `Baselines/messages/contracts.messages.json` | an `IIntegrationEvent` is removed/renamed, its MassTransit URN (namespace/type name) changes, a property is removed, renamed, retyped, becomes nullable or becomes required, or a new property is not nullable |
| Golden payloads | `Baselines/messages/samples/*.json` | a payload serialized from the baseline contract no longer round-trips into the current type |
| Proto schema (descriptor-based `buf breaking`) | `Baselines/grpc/*.json` | a package, `csharp_namespace`, service, rpc, message or enum is removed/renamed; an rpc's request/response/streaming changes; a field is removed (even when its number and name are reserved), renamed, renumbered, retyped, or changes cardinality, oneof or proto3 `optional` presence; a reserved number/name is reused; an enum value is removed or renumbered. Versioned files (`Protos/v2/flight.proto`) get their own baseline (`grpc/v2/flight.json`) |
| Consumer pacts | `Pacts/grpc/*.json`, `Pacts/messages/*.json` | a provider drops or changes an rpc/field/property a consumer declared it reads, or a module adds an `IConsumer<T>` for a shared contract without a pact |
| Negative runs | `BreakingChangeDetectionTests.cs` | the rules above stop detecting known-breaking edits applied to the real descriptors/types |

Additive changes (new message, property, rpc, field number, enum value) pass the compatibility check but
`*_baseline_is_up_to_date` fails until the baseline is refreshed, so the next change is compared against it:

```bash
UPDATE_CONTRACT_BASELINES=true dotnet test tests/Contracts.Compatibility.Test
git diff tests/Contracts.Compatibility.Test/Baselines
```

The update mode only rewrites a baseline when the change is compatible and never rewrites existing golden
samples. A breaking change has to ship as a new versioned message type / proto package instead.

## Negative run (real contract edit)

Changing `double Price = 11;` to `float Price = 11;` in `flight.proto` and `string Name` to `string? Name`
in `UserCreated` fails the suite:

```text
Failed!  - Failed:     7, Passed:    37, Skipped:     0, Total:    44
flight.proto: field .flight.FlightResponse.Price = 11: type changed from Double to Float
booking (...CreateBookingCommandHandler) reads .flight.FlightResponse.Price = 11 as Optional Double, provider has Optional Float Price
message BuildingBlocks.Contracts.EventBus.Messages.UserCreated: property Name became nullable
```
