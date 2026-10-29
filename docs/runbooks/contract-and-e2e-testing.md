# Contract and cross-service testing

## Run contract checks

From the repository root:

```bash
dotnet test src/Contracts/tests/Contracts.Grpc.Tests
dotnet test src/Contracts/tests/Contracts.Messages.Tests
dotnet test src/Contracts/tests/Contracts.ConsumerDriven.Tests
```

The protobuf baseline is at `src/Contracts/tests/Contracts.Grpc.Tests/Baselines/grpc-contract.json`. After reviewing a compatible descriptor change, regenerate it with:

```bash
UPDATE_CONTRACT_BASELINES=true dotnet test src/Contracts/tests/Contracts.Grpc.Tests
```

Consumer-owned pacts are stored under `src/Contracts/pacts/`. Update a pact when its consumer changes its actual dependency, and verify that the change remains compatible with the provider before merging.

## Run the process-level suite

Docker must be available. The suite starts PostgreSQL, MongoDB, RabbitMQ, and EventStoreDB containers, then launches each service host and the Gateway as a separate `dotnet` process:

```bash
dotnet test src/Tests/CrossService.EndToEnd.Test/CrossService.EndToEnd.Test.csproj
```

The test harness writes process stdout and stderr to `src/Tests/CrossService.EndToEnd.Test/bin/<configuration>/net10.0/TestResults/e2e-logs/`. On failure, assertion output includes recent lines from every host log. The tests use dynamic loopback ports and do not support an environment-variable skip.

## Database ownership in host tests

Host.Test fixtures use `<module>_service` databases and module Integration.Test fixtures continue to use `<module>_modular_monolith`. The test PostgreSQL container initializes both sets. RabbitMQ transport checks use the RabbitMQ management API to bind a temporary probe queue to the integration-event exchange.
