FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/BuildingBlocks/Mongo/MicroBootstrap.Persistence.Mongo.csproj src/BuildingBlocks/Mongo/
COPY src/Modules/Passenger/src/Passenger.csproj src/Modules/Passenger/src/
COPY src/SharedContracts/SharedContracts.csproj src/SharedContracts/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Services/Passenger.Api/Passenger.Api.csproj src/Services/Passenger.Api/

RUN dotnet restore src/Services/Passenger.Api/Passenger.Api.csproj

COPY src ./src

RUN dotnet build src/Services/Passenger.Api/Passenger.Api.csproj -c Release --no-restore

RUN dotnet publish src/Services/Passenger.Api/Passenger.Api.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 8080
EXPOSE 5004

ENTRYPOINT ["dotnet", "Passenger.Api.dll"]
