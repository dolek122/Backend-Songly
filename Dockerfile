FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first, in its own layer: it only reruns when a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Songly.Api/Songly.Api.csproj src/Songly.Api/
COPY src/Songly.Common/Songly.Common.csproj src/Songly.Common/
COPY src/Songly.Migrations/Songly.Migrations.csproj src/Songly.Migrations/
COPY src/Modules/ src/Modules/
RUN dotnet restore src/Songly.Api/Songly.Api.csproj

COPY src/ src/
RUN dotnet publish src/Songly.Api/Songly.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The base image ships a non-root user for exactly this.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Songly.Api.dll"]
