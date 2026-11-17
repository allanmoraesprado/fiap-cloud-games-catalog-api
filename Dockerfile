# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first for better layer caching
COPY src/CatalogApi/CatalogApi.csproj src/CatalogApi/
RUN dotnet restore src/CatalogApi/CatalogApi.csproj

# Copy sources and publish
COPY src/ src/
RUN dotnet publish src/CatalogApi/CatalogApi.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
# Commit SHA of the build (set by CI: --build-arg APP_VERSION=<sha>); exposed to the app and as
# OCI metadata so a running pod can be traced back to its commit.
ARG APP_VERSION=dev
ENV APP_VERSION=$APP_VERSION
LABEL org.opencontainers.image.revision=$APP_VERSION \
      org.opencontainers.image.source="https://github.com/allanmoraesprado/fiap-cloud-games-catalog-api"
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish .
EXPOSE 8080
# Run as the built-in non-root user by numeric UID (APP_UID=1654 in the .NET images), so
# Kubernetes can enforce runAsNonRoot.
USER $APP_UID
ENTRYPOINT ["dotnet", "CatalogApi.dll"]
