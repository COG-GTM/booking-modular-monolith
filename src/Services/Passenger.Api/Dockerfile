FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Services/Passenger.Api/src/Passenger.Api.csproj src/Services/Passenger.Api/src/

RUN dotnet restore src/Services/Passenger.Api/src/Passenger.Api.csproj

COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Aspire/src/ServiceDefaults src/Aspire/src/ServiceDefaults
COPY src/Services/Passenger.Api/src src/Services/Passenger.Api/src

RUN dotnet build src/Services/Passenger.Api/src/Passenger.Api.csproj -c Release --no-restore
RUN dotnet publish src/Services/Passenger.Api/src/Passenger.Api.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80
EXPOSE 81

ENTRYPOINT ["dotnet", "Passenger.Api.dll"]
