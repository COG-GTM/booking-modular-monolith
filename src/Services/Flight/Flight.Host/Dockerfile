FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .
COPY src ./src

RUN dotnet restore src/Services/Flight/Flight.Host/Flight.Host.csproj
RUN dotnet build src/Services/Flight/Flight.Host/Flight.Host.csproj -c Release --no-restore
RUN dotnet publish src/Services/Flight/Flight.Host/Flight.Host.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80 443

ENTRYPOINT ["dotnet", "Flight.Host.dll"]
