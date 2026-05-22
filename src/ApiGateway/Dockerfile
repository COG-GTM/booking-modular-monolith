FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY .editorconfig .
COPY global.json .
COPY Directory.Build.props .

COPY src/ApiGateway/ApiGateway.csproj src/ApiGateway/

RUN dotnet restore src/ApiGateway/ApiGateway.csproj

COPY src/ApiGateway ./src/ApiGateway

RUN dotnet build src/ApiGateway/ApiGateway.csproj -c Release --no-restore

RUN dotnet publish src/ApiGateway/ApiGateway.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
