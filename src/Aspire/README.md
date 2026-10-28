# Aspire local orchestration

Run the AppHost from the repository root with `dotnet run --project src/Aspire/src/AppHost`, or
from `src/Aspire/src/AppHost` with `dotnet aspire run`.

`AppHost:Topology` defaults to `Services` and starts the gateway plus the Identity, Flight,
Passenger, and Booking hosts. Use `--AppHost:Topology=Monolith` to run the existing API plus gateway
instead. Do not run both topologies against the same databases at once.

| Resource | Configuration supplied by Aspire |
| --- | --- |
| identity-service | Identity and persist-message PostgreSQL connection strings, RabbitMQ, issuer and JWT metadata |
| flight-service | Flight and persist-message PostgreSQL connection strings, MongoDB, RabbitMQ, JWT metadata |
| passenger-service | Passenger and persist-message PostgreSQL connection strings, MongoDB, RabbitMQ, JWT metadata |
| booking-service | Booking persist-message PostgreSQL connection string, MongoDB, EventStoreDB, RabbitMQ, gRPC service discovery |
| gateway | Service endpoint references, JWT authority and metadata, YARP cluster destinations |

The gateway is available at `http://localhost:5000` and the Aspire dashboard at
`http://localhost:18888`. Service and infrastructure ports are otherwise dynamic except for
ports already configured by the service hosts.

In Services topology, the gateway's `monolith` fallback points to Identity because no monolith
process is started. The fallback requires authentication so unmatched Identity paths cannot bypass
gateway authorization; `/connect/**` and `/.well-known/**` keep their dedicated anonymous routes.
Monolith topology retains the legacy API destination for every gateway cluster.
