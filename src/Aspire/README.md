# Aspire local orchestration

Run the AppHost from the repository root with `dotnet run --project src/Aspire/src/AppHost`, or
from `src/Aspire/src/AppHost` with `dotnet aspire run`.

AppHost starts only the gateway plus the Identity, Flight, Passenger, and Booking hosts.
There is no topology selector or monolith project. Previous-release recovery uses the
[cutover rollback overlay](../../docs/runbooks/cutover/decommission.md), not AppHost.

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

Aspire sets `OTEL_SERVICE_NAME` to each project resource name. AppHost sends OTLP telemetry to the
AB-495 `otel-collector` endpoint using `OTEL_EXPORTER_OTLP_ENDPOINT` and
`OTEL_EXPORTER_OTLP_PROTOCOL=grpc`. The collector forwards traces to Tempo and Jaeger, metrics to
Prometheus, and logs to Loki; Grafana is available at `http://localhost:3030`. The Aspire dashboard
shows resources, health, and console logs, but not service traces or metrics. Forwarding collector
telemetry to the Aspire dashboard is a follow-up owned by AB-495's collector configuration.

The gateway has only the four service clusters and no fallback route. Unknown paths return 404;
`/connect/**` and `/.well-known/**` keep their dedicated anonymous Identity routes.
