# Contracts.Grpc

Shared protobuf contracts for the Flight and Passenger gRPC services. The wire packages and
service names are `flight.FlightGrpcService` and `passenger.PassengerGrpcService`.

Booking configures service targets with `Grpc:FlightAddress` and `Grpc:PassengerAddress`.
The monolith uses its own HTTPS endpoint; Aspire injects the `api-https` endpoint. Extracted
hosts can use their service DNS names (for example, `http://flight-service:81`).

Keep wire changes additive; follow the versioning policy in
[ADR 0005](../../../docs/adr/0005-contract-and-proto-versioning-policy.md).
