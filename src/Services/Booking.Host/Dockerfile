FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props .editorconfig ./
COPY src ./src
RUN dotnet restore src/Services/Booking.Host/Booking.Host.csproj
RUN dotnet publish src/Services/Booking.Host/Booking.Host.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Docker
EXPOSE 8080
ENTRYPOINT ["dotnet", "Booking.Host.dll"]
