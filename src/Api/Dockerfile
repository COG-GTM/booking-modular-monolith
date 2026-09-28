FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/
COPY src/Api/src/Api.csproj src/Api/src/

RUN dotnet restore src/Api/src/Api.csproj

COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Aspire/src/ServiceDefaults src/Aspire/src/ServiceDefaults
COPY src/Api/src src/Api/src

RUN dotnet build src/Api/src/Api.csproj -c Release --no-restore
RUN dotnet publish src/Api/src/Api.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Api.dll"]
