FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .
COPY src ./src

RUN dotnet restore src/Services/Flight/FlightService.csproj

RUN dotnet publish src/Services/Flight/FlightService.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 5101 5111

ENTRYPOINT ["dotnet", "FlightService.dll"]
