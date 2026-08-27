FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

# Copy project files first (better Docker layer caching)
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/BuildingBlocks/Hosting/Shared.Hosting.csproj src/BuildingBlocks/Hosting/
COPY src/Modules/Flight/src/Flight.csproj src/Modules/Flight/src/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Services/Flight/Flight.Api/Flight.Api.csproj src/Services/Flight/Flight.Api/

# Restore packages
RUN dotnet restore src/Services/Flight/Flight.Api/Flight.Api.csproj

# Copy only the sources this service needs
COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Modules/Flight src/Modules/Flight
COPY src/Aspire/src/ServiceDefaults src/Aspire/src/ServiceDefaults
COPY src/Services/Flight src/Services/Flight

# Build
RUN dotnet build src/Services/Flight/Flight.Api/Flight.Api.csproj -c Release --no-restore

# Publish
RUN dotnet publish src/Services/Flight/Flight.Api/Flight.Api.csproj -c Release --no-build -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80
EXPOSE 81

ENTRYPOINT ["dotnet", "Flight.Api.dll"]
