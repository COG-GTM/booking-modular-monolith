# Contracts.Grpc

Single source of truth for the synchronous gRPC contracts between services. Flight and Passenger
host the generated server bases; Booking uses the generated clients. Do not copy these `.proto`
files into modules — reference this project (or the `Contracts.Grpc` package) instead.

| Proto | Package | C# namespace | Server | Clients |
| --- | --- | --- | --- | --- |
| `Protos/flight.proto` | `flight` | `Contracts.Grpc.Flight` | Flight | Booking |
| `Protos/passenger.proto` | `passenger` | `Contracts.Grpc.Passenger` | Passenger | Booking |

The proto `package` is part of every method path on the wire (`/flight.FlightGrpcService/GetById`),
so client and server must always compile the same file.

## Versioning (additive only)

The package is versioned with SemVer (`<Version>` in `Contracts.Grpc.csproj`).

Allowed without coordination (minor/patch bump):

- add a new field with a new, never-used tag number;
- add a new message, enum value, RPC or service.

Never do this in an existing package:

- remove, rename or renumber a field, or change its type;
- reuse a tag number (mark removed fields `reserved` instead);
- change an RPC's request/response type or remove an RPC.

A breaking change is a new versioned contract alongside the old one (e.g. `package flight.v2;` in
`Protos/v2/flight.proto`), served in parallel until every client has migrated; then the old package
can be retired (major bump).

## Consuming

```xml
<ProjectReference Include="..\..\..\Contracts\Contracts.Grpc\Contracts.Grpc.csproj" />
```

`dotnet pack src/Contracts/Contracts.Grpc -o artifacts/packages` produces the NuGet package (generated
code plus the `.proto` files under `protos/`) for services built outside this solution.

Client addressing is configured in the consuming service, not here (Booking: `Grpc:FlightAddress`,
`Grpc:PassengerAddress`).
