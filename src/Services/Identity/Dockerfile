FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

COPY global.json Directory.Build.props .editorconfig ./
COPY src ./src

RUN dotnet restore src/Services/Identity/src/Identity.Host.csproj
RUN dotnet build src/Services/Identity/src/Identity.Host.csproj -c Release --no-restore
RUN dotnet publish src/Services/Identity/src/Identity.Host.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY --from=builder /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=docker

EXPOSE 80

ENTRYPOINT ["dotnet", "Identity.Host.dll"]
