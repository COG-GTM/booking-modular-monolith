FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .
COPY nuget.config .

# Copy project files first (better Docker layer caching)
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
COPY src/Modules/Booking/src/Booking.csproj src/Modules/Booking/src/
COPY src/Modules/Flight/src/Flight.csproj src/Modules/Flight/src/
COPY src/Modules/Identity/src/Identity.csproj src/Modules/Identity/src/
COPY src/Modules/Passenger/src/Passenger.csproj src/Modules/Passenger/src/
COPY src/Api/src/Api.csproj src/Api/src/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/

# Restore packages
RUN dotnet restore src/Api/src/Api.csproj

# Copy everything else
COPY src ./src

# Build
RUN dotnet build src/Api/src/Api.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Api/src/Api.csproj -c Release --no-build -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Api.dll"]