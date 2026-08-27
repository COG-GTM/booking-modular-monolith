FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

# Copy project files first (better Docker layer caching)
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/BuildingBlocks/Hosting/Shared.Hosting.csproj src/BuildingBlocks/Hosting/
COPY src/Modules/Passenger/src/Passenger.csproj src/Modules/Passenger/src/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Services/Passenger/Passenger.Api/Passenger.Api.csproj src/Services/Passenger/Passenger.Api/

# Restore packages
RUN dotnet restore src/Services/Passenger/Passenger.Api/Passenger.Api.csproj

# Copy only the sources this service needs
COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Modules/Passenger src/Modules/Passenger
COPY src/Aspire/src/ServiceDefaults src/Aspire/src/ServiceDefaults
COPY src/Services/Passenger src/Services/Passenger

# Build
RUN dotnet build src/Services/Passenger/Passenger.Api/Passenger.Api.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Services/Passenger/Passenger.Api/Passenger.Api.csproj -c Release --no-build -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Passenger.Api.dll"]
