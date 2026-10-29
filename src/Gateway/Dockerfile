FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

# Copy project files first (better Docker layer caching)
COPY src/BuildingBlocks/Directory.Build.props src/BuildingBlocks/
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/BuildingBlocks/Caching/BuildingBlocks.Caching.csproj src/BuildingBlocks/Caching/
COPY src/BuildingBlocks/Constants/BuildingBlocks.Constants.csproj src/BuildingBlocks/Constants/
COPY src/BuildingBlocks/Contracts/BuildingBlocks.Contracts.csproj src/BuildingBlocks/Contracts/
COPY src/BuildingBlocks/Core/BuildingBlocks.Core.csproj src/BuildingBlocks/Core/
COPY src/BuildingBlocks/EFCore/BuildingBlocks.EFCore.csproj src/BuildingBlocks/EFCore/
COPY src/BuildingBlocks/EventStoreDB/BuildingBlocks.EventStoreDB.csproj src/BuildingBlocks/EventStoreDB/
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
COPY src/BuildingBlocks/TestBase/BuildingBlocks.TestBase.csproj src/BuildingBlocks/TestBase/
COPY src/BuildingBlocks/Utils/BuildingBlocks.Utils.csproj src/BuildingBlocks/Utils/
COPY src/BuildingBlocks/Validation/BuildingBlocks.Validation.csproj src/BuildingBlocks/Validation/
COPY src/BuildingBlocks/Web/BuildingBlocks.Web.csproj src/BuildingBlocks/Web/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Gateway/src/Gateway.csproj src/Gateway/src/

# Restore packages
RUN dotnet restore src/Gateway/src/Gateway.csproj

# Copy only what the gateway compiles
COPY src/BuildingBlocks ./src/BuildingBlocks
COPY src/Aspire/src/ServiceDefaults ./src/Aspire/src/ServiceDefaults
COPY src/Gateway/src ./src/Gateway/src

# Build
RUN dotnet build src/Gateway/src/Gateway.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Gateway/src/Gateway.csproj -c Release --no-build -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Gateway.dll"]
