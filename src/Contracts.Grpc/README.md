# Contracts.Grpc

Single source of truth for the gRPC contracts exchanged between the booking modules/services.
Servers (`Flight`, `Passenger`) and clients (`Booking`) reference this project (or the published
`Contracts.Grpc` package) instead of keeping their own copies of the `.proto` files.

| Proto | Package | C# namespace | Service |
| --- | --- | --- | --- |
| `Protos/flight/v1/flight.proto` | `flight.v1` | `Contracts.Grpc.Flight.V1` | `flight.v1.FlightGrpcService` |
| `Protos/passenger/v1/passenger.proto` | `passenger.v1` | `Contracts.Grpc.Passenger.V1` | `passenger.v1.PassengerGrpcService` |

## Versioning (ADR 0005)

- The package follows SemVer: patch/minor = backward-compatible additions, major = breaking.
- Within a proto package version only backward-compatible changes are allowed: add fields with new
  tag numbers, add RPCs, add messages. Never reuse or renumber tags; mark removed fields `reserved`.
- Breaking changes go into a new proto package (`flight.v2`, `Protos/flight/v2/...`) served side by
  side with the previous version until every consumer has migrated.
