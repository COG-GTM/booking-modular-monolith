FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

# Copy only what the Booking service needs for restore (better Docker layer caching)
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Contracts.Grpc/Contracts.Grpc.csproj src/Contracts.Grpc/
COPY src/Contracts.Messages/Contracts.Messages.csproj src/Contracts.Messages/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Modules/Booking/src/Booking.csproj src/Modules/Booking/src/
COPY src/Services/Booking/src/Booking.Host.csproj src/Services/Booking/src/

# Restore packages
RUN dotnet restore src/Services/Booking/src/Booking.Host.csproj

# Copy the sources the Booking service is built from
COPY src/Aspire/src/ServiceDefaults ./src/Aspire/src/ServiceDefaults
COPY src/Contracts.Grpc ./src/Contracts.Grpc
COPY src/Contracts.Messages ./src/Contracts.Messages
COPY src/Modules/Booking/src ./src/Modules/Booking/src
COPY src/Services/Booking/src ./src/Services/Booking/src

# Build
RUN dotnet build src/Services/Booking/src/Booking.Host.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Services/Booking/src/Booking.Host.csproj -c Release --no-build -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Booking.Host.dll"]
