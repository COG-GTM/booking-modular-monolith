FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/Contracts/Directory.Build.props src/Contracts/Versions.props src/Contracts/Contracts.Messages.Reference.props src/Contracts/Contracts.Grpc.Reference.props src/Contracts/
COPY src/Contracts/Contracts.Messages/Contracts.Messages.csproj src/Contracts/Contracts.Messages/
COPY src/Contracts/Contracts.Grpc/Contracts.Grpc.csproj src/Contracts/Contracts.Grpc/
COPY src/BuildingBlocks/Directory.Build.props src/BuildingBlocks/
COPY src/BuildingBlocks/Caching/BuildingBlocks.Caching.csproj src/BuildingBlocks/Caching/
COPY src/BuildingBlocks/Constants/BuildingBlocks.Constants.csproj src/BuildingBlocks/Constants/
COPY src/BuildingBlocks/Core/BuildingBlocks.Core.csproj src/BuildingBlocks/Core/
COPY src/BuildingBlocks/EFCore/BuildingBlocks.EFCore.csproj src/BuildingBlocks/EFCore/
COPY src/BuildingBlocks/Exception/BuildingBlocks.Exception.csproj src/BuildingBlocks/Exception/
COPY src/BuildingBlocks/HealthCheck/BuildingBlocks.HealthCheck.csproj src/BuildingBlocks/HealthCheck/
COPY src/BuildingBlocks/Jwt/BuildingBlocks.Jwt.csproj src/BuildingBlocks/Jwt/
COPY src/BuildingBlocks/Logging/BuildingBlocks.Logging.csproj src/BuildingBlocks/Logging/
COPY src/BuildingBlocks/Mapster/BuildingBlocks.Mapster.csproj src/BuildingBlocks/Mapster/
COPY src/BuildingBlocks/MassTransit/BuildingBlocks.MassTransit.csproj src/BuildingBlocks/MassTransit/
COPY src/BuildingBlocks/Mongo/BuildingBlocks.Mongo.csproj src/BuildingBlocks/Mongo/
COPY src/BuildingBlocks/OpenApi/BuildingBlocks.OpenApi.csproj src/BuildingBlocks/OpenApi/
COPY src/BuildingBlocks/OpenTelemetryCollector/BuildingBlocks.OpenTelemetryCollector.csproj src/BuildingBlocks/OpenTelemetryCollector/
COPY src/BuildingBlocks/PersistMessageProcessor/BuildingBlocks.PersistMessageProcessor.csproj src/BuildingBlocks/PersistMessageProcessor/
COPY src/BuildingBlocks/Polly/BuildingBlocks.Polly.csproj src/BuildingBlocks/Polly/
COPY src/BuildingBlocks/ProblemDetails/BuildingBlocks.ProblemDetails.csproj src/BuildingBlocks/ProblemDetails/
COPY src/BuildingBlocks/Utils/BuildingBlocks.Utils.csproj src/BuildingBlocks/Utils/
COPY src/BuildingBlocks/Validation/BuildingBlocks.Validation.csproj src/BuildingBlocks/Validation/
COPY src/BuildingBlocks/Web/BuildingBlocks.Web.csproj src/BuildingBlocks/Web/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Modules/Passenger/src/Passenger.csproj src/Modules/Passenger/src/
COPY src/Services/Passenger.Host/Passenger.Host.csproj src/Services/Passenger.Host/

RUN dotnet restore src/Services/Passenger.Host/Passenger.Host.csproj

COPY src/Contracts/Contracts.Messages ./src/Contracts/Contracts.Messages
COPY src/Contracts/Contracts.Grpc ./src/Contracts/Contracts.Grpc
COPY src/BuildingBlocks/Caching ./src/BuildingBlocks/Caching
COPY src/BuildingBlocks/Constants ./src/BuildingBlocks/Constants
COPY src/BuildingBlocks/Core ./src/BuildingBlocks/Core
COPY src/BuildingBlocks/EFCore ./src/BuildingBlocks/EFCore
COPY src/BuildingBlocks/Exception ./src/BuildingBlocks/Exception
COPY src/BuildingBlocks/HealthCheck ./src/BuildingBlocks/HealthCheck
COPY src/BuildingBlocks/Jwt ./src/BuildingBlocks/Jwt
COPY src/BuildingBlocks/Logging ./src/BuildingBlocks/Logging
COPY src/BuildingBlocks/Mapster ./src/BuildingBlocks/Mapster
COPY src/BuildingBlocks/MassTransit ./src/BuildingBlocks/MassTransit
COPY src/BuildingBlocks/Mongo ./src/BuildingBlocks/Mongo
COPY src/BuildingBlocks/OpenApi ./src/BuildingBlocks/OpenApi
COPY src/BuildingBlocks/OpenTelemetryCollector ./src/BuildingBlocks/OpenTelemetryCollector
COPY src/BuildingBlocks/PersistMessageProcessor ./src/BuildingBlocks/PersistMessageProcessor
COPY src/BuildingBlocks/Polly ./src/BuildingBlocks/Polly
COPY src/BuildingBlocks/ProblemDetails ./src/BuildingBlocks/ProblemDetails
COPY src/BuildingBlocks/Utils ./src/BuildingBlocks/Utils
COPY src/BuildingBlocks/Validation ./src/BuildingBlocks/Validation
COPY src/BuildingBlocks/Web ./src/BuildingBlocks/Web
COPY src/Aspire/src/ServiceDefaults ./src/Aspire/src/ServiceDefaults
COPY src/Modules/Passenger/src ./src/Modules/Passenger/src
COPY src/Services/Passenger.Host ./src/Services/Passenger.Host

RUN dotnet build src/Services/Passenger.Host/Passenger.Host.csproj -c Release --no-restore
RUN dotnet publish src/Services/Passenger.Host/Passenger.Host.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80 81

ENTRYPOINT ["dotnet", "Passenger.Host.dll"]
