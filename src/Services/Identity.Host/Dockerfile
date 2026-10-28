FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props .editorconfig ./
# Include the complete project graph and per-directory build props before restore.
COPY src ./src
RUN dotnet restore src/Services/Identity.Host/Identity.Host.csproj
RUN dotnet publish src/Services/Identity.Host/Identity.Host.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=docker
EXPOSE 8080
ENTRYPOINT ["dotnet", "Identity.Host.dll"]
