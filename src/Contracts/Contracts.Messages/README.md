# BookingModularMonolith.Contracts.Messages

Integration-event contracts exchanged between the booking services over MassTransit
(in-memory in the monolith, RabbitMQ between split services).

| Producer  | Messages                                                                                                   |
|-----------|------------------------------------------------------------------------------------------------------------|
| Identity  | `UserCreated`                                                                                              |
| Flight    | `FlightCreated`, `FlightUpdated`, `FlightDeleted`, `AircraftCreated`, `AirportCreated`, `SeatCreated`, `SeatReserved` |
| Passenger | `PassengerCreated`, `PassengerRegistrationCompleted`                                                       |
| Booking   | `BookingCreated`                                                                                           |

All messages live in the `BuildingBlocks.Contracts.EventBus.Messages` namespace and implement
`BuildingBlocks.Core.Event.IIntegrationEvent`. The namespaces are kept from the original
BuildingBlocks location on purpose: MassTransit derives message URNs / RabbitMQ exchange names
and the outbox derives `DataType` from the full type name, so renaming them is a breaking wire change.

The package depends only on `MassTransit.Abstractions` and `MediatR.Contracts` — never on
BuildingBlocks or module code.

## Versioning

SemVer. Changes are additive only; breaking changes ship as a new `V2`-suffixed message type.
See [docs/contracts/compatibility-policy.md](../../../docs/contracts/compatibility-policy.md).

## Packing

```bash
dotnet pack src/Contracts/Contracts.Messages/Contracts.Messages.csproj -c Release -o artifacts/packages
```
