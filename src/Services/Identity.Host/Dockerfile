FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Copy solution-level files
COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .
COPY nuget.config .

# Copy only the inputs Identity.Host builds from (no other modules, no monolith host)
COPY src/BuildingBlocks ./src/BuildingBlocks
COPY src/Contracts/Contracts.Messages ./src/Contracts/Contracts.Messages
COPY src/Modules/Identity/src ./src/Modules/Identity/src
COPY src/Services/Identity.Host/src ./src/Services/Identity.Host/src

RUN dotnet restore src/Services/Identity.Host/src/Identity.Host.csproj

RUN dotnet publish src/Services/Identity.Host/src/Identity.Host.csproj -c Release --no-restore -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Identity.Host.dll"]
