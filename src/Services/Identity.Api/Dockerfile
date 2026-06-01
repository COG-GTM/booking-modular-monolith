FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Modules/Identity/src/Identity.csproj src/Modules/Identity/src/
COPY src/Services/Identity.Api/Identity.Api.csproj src/Services/Identity.Api/
COPY src/Aspire/src/ServiceDefaults/ServiceDefaults.csproj src/Aspire/src/ServiceDefaults/

RUN dotnet restore src/Services/Identity.Api/Identity.Api.csproj

COPY src ./src

RUN dotnet build src/Services/Identity.Api/Identity.Api.csproj -c Release --no-restore
RUN dotnet publish src/Services/Identity.Api/Identity.Api.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Identity.Api.dll"]
