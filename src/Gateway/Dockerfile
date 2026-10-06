FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

# Copy project files first (better Docker layer caching)
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Gateway/src/Gateway.csproj src/Gateway/src/

# Restore packages
RUN dotnet restore src/Gateway/src/Gateway.csproj

# Copy everything else
COPY src/BuildingBlocks ./src/BuildingBlocks
COPY src/Aspire/src/ServiceDefaults ./src/Aspire/src/ServiceDefaults
COPY src/Gateway ./src/Gateway

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
