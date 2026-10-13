FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .
COPY nuget.config .

# Copy project files first (better Docker layer caching). ServiceDefaults references the BuildingBlocks
# aggregate, so every BuildingBlocks project and Contracts.Messages must be present for restore.
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/Directory.Build.props src/BuildingBlocks/
COPY src/BuildingBlocks/Core/BuildingBlocks.Core.csproj src/BuildingBlocks/Core/
COPY src/BuildingBlocks/EFCore/BuildingBlocks.EFCore.csproj src/BuildingBlocks/EFCore/
COPY src/BuildingBlocks/EventStoreDB/BuildingBlocks.EventStoreDB.csproj src/BuildingBlocks/EventStoreDB/
COPY src/BuildingBlocks/HealthCheck/BuildingBlocks.HealthCheck.csproj src/BuildingBlocks/HealthCheck/
COPY src/BuildingBlocks/Jwt/BuildingBlocks.Jwt.csproj src/BuildingBlocks/Jwt/
COPY src/BuildingBlocks/MassTransit/BuildingBlocks.MassTransit.csproj src/BuildingBlocks/MassTransit/
COPY src/BuildingBlocks/Mongo/BuildingBlocks.Mongo.csproj src/BuildingBlocks/Mongo/
COPY src/BuildingBlocks/OpenTelemetryCollector/BuildingBlocks.OpenTelemetryCollector.csproj src/BuildingBlocks/OpenTelemetryCollector/
COPY src/BuildingBlocks/PersistMessageProcessor/BuildingBlocks.PersistMessageProcessor.csproj src/BuildingBlocks/PersistMessageProcessor/
COPY src/BuildingBlocks/ProblemDetails/BuildingBlocks.ProblemDetails.csproj src/BuildingBlocks/ProblemDetails/
COPY src/BuildingBlocks/Validation/BuildingBlocks.Validation.csproj src/BuildingBlocks/Validation/
COPY src/BuildingBlocks/Web/BuildingBlocks.Web.csproj src/BuildingBlocks/Web/
COPY src/BuildingBlocks/TestBase/BuildingBlocks.TestBase.csproj src/BuildingBlocks/TestBase/
COPY src/Contracts/Contracts.Messages/Contracts.Messages.csproj src/Contracts/Contracts.Messages/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Gateway/src/Gateway.csproj src/Gateway/src/

# Restore packages
RUN dotnet restore src/Gateway/src/Gateway.csproj

# Copy only the sources the gateway needs
COPY src/BuildingBlocks ./src/BuildingBlocks
COPY src/Contracts/Contracts.Messages ./src/Contracts/Contracts.Messages
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
EXPOSE 443

ENTRYPOINT ["dotnet", "Gateway.dll"]
