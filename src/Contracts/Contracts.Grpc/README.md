# Contracts.Grpc

Single source of truth for the synchronous service-to-service contracts:

| Proto | Package | Service | Provider | Consumers |
|---|---|---|---|---|
| `Protos/flight.proto` | `flight` | `flight.FlightGrpcService` | Flight | Booking |
| `Protos/passenger.proto` | `passenger` | `passenger.PassengerGrpcService` | Passenger | Booking |

The project generates both client and server stubs (C# namespaces `Flight` / `Passenger`) and ships the `.proto`
files under `protos/` in the NuGet package (`BookingModularMonolith.Contracts.Grpc`) for non-.NET tooling.

Rules (ADR 0102):

- Never change a proto package, service or RPC name, field number or field type, or reuse a removed tag.
- Additive changes (new optional fields, new RPCs) bump the minor `ContractsGrpcVersion` in `src/Contracts/Versions.props` and update
  `tests/Contracts.Grpc.Tests/contract.snapshot.txt` (`UPDATE_CONTRACT_SNAPSHOT=1 dotnet test src/Contracts/tests/Contracts.Grpc.Tests`).
- A breaking change is a new package (`flight.v2`) served side by side until every consumer has moved.
- Do not add `.proto` files anywhere else; `WireContractTests` fails if one appears.

## Consuming

Providers and consumers import `src/Contracts/Contracts.Grpc.Reference.props` instead of referencing the project:
a project reference by default, or `-p:UseContractsGrpcPackage=true` for the pinned
`BookingModularMonolith.Contracts.Grpc` package (`ContractsGrpcPackageVersion` overrides the pin).
