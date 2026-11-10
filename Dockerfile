# syntax=docker/dockerfile:1
# Hyundai Stock web (MVC dashboard + mobile API) for Cloudflare Containers.
# Build:  docker build -t hyundai-stock .
# Kestrel listens on loopback only (see docs/LIVE-READINESS.md); a socat
# forwarder exposes it on :8080 once the app is ready, so the trusted-proxy
# rules for X-Forwarded-Proto keep working unchanged.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY HyundaiStock.sln ./
COPY src/HyundaiStock.Domain/HyundaiStock.Domain.csproj src/HyundaiStock.Domain/
COPY src/HyundaiStock.Infrastructure/HyundaiStock.Infrastructure.csproj src/HyundaiStock.Infrastructure/
COPY src/HyundaiStock.Web/HyundaiStock.Web.csproj src/HyundaiStock.Web/
RUN dotnet restore src/HyundaiStock.Web/HyundaiStock.Web.csproj
COPY src/ src/
RUN dotnet publish src/HyundaiStock.Web/HyundaiStock.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends socat fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY deploy/container/entrypoint.sh /usr/local/bin/hyundai-entrypoint
RUN chmod 0755 /usr/local/bin/hyundai-entrypoint \
 && mkdir -p /app/App_Data/uploads \
 && chown -R app:app /app/App_Data

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://127.0.0.1:5169 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0 \
    Database__Provider=SqlServer \
    PUBLIC_PORT=8080 \
    APP_PORT=5169

USER app
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/hyundai-entrypoint"]
