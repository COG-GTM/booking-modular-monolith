FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

ARG SERVICE_DIR
ARG SERVICE_NAME

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

# Copy project files first (better Docker layer caching)
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/BuildingBlocks/Contracts/EventBus.Messages/EventBus.Messages.csproj src/BuildingBlocks/Contracts/EventBus.Messages/
COPY src/BuildingBlocks/Contracts/Grpc.Contracts/Grpc.Contracts.csproj src/BuildingBlocks/Contracts/Grpc.Contracts/
COPY src/Modules/Booking/src/Booking.csproj src/Modules/Booking/src/
COPY src/Modules/Flight/src/Flight.csproj src/Modules/Flight/src/
COPY src/Modules/Identity/src/Identity.csproj src/Modules/Identity/src/
COPY src/Modules/Passenger/src/Passenger.csproj src/Modules/Passenger/src/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Services/Flight/FlightService.csproj src/Services/Flight/
COPY src/Services/Passenger/PassengerService.csproj src/Services/Passenger/
COPY src/Services/Identity/IdentityService.csproj src/Services/Identity/
COPY src/Services/Booking/BookingService.csproj src/Services/Booking/
COPY src/Services/Gateway/GatewayService.csproj src/Services/Gateway/

# Restore packages
RUN dotnet restore src/Services/${SERVICE_DIR}/${SERVICE_NAME}.csproj

# Copy everything else
COPY src ./src

# Build
RUN dotnet build src/Services/${SERVICE_DIR}/${SERVICE_NAME}.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Services/${SERVICE_DIR}/${SERVICE_NAME}.csproj -c Release --no-build -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

ARG SERVICE_NAME
ENV SERVICE_DLL=${SERVICE_NAME}.dll

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT dotnet ${SERVICE_DLL}
