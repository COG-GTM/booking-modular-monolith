FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/BuildingBlocks/Mongo/MicroBootstrap.Persistence.Mongo.csproj src/BuildingBlocks/Mongo/
COPY src/Modules/Booking/src/Booking.csproj src/Modules/Booking/src/
COPY src/SharedContracts/SharedContracts.csproj src/SharedContracts/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Services/Booking.Api/Booking.Api.csproj src/Services/Booking.Api/

RUN dotnet restore src/Services/Booking.Api/Booking.Api.csproj

COPY src ./src

RUN dotnet build src/Services/Booking.Api/Booking.Api.csproj -c Release --no-restore

RUN dotnet publish src/Services/Booking.Api/Booking.Api.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 8080

ENTRYPOINT ["dotnet", "Booking.Api.dll"]
