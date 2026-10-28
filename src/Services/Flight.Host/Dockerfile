FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props .editorconfig ./
COPY src ./src
RUN dotnet restore src/Services/Flight.Host/Flight.Host.csproj
RUN dotnet publish src/Services/Flight.Host/Flight.Host.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=docker
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "Flight.Host.dll"]
