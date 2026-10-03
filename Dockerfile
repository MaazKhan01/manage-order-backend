# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, on their own layer.
#
# Restore is the slow step and it only depends on these. Copying the whole tree first would
# invalidate the restore cache on every source edit and turn a 20-second rebuild into a 3-minute
# one - which matters most on a free-tier builder, where CPU is scarce.
COPY Directory.Packages.props Directory.Build.props* ./
COPY src/DmOrder.Domain/DmOrder.Domain.csproj src/DmOrder.Domain/
COPY src/DmOrder.Application/DmOrder.Application.csproj src/DmOrder.Application/
COPY src/DmOrder.Infrastructure/DmOrder.Infrastructure.csproj src/DmOrder.Infrastructure/
COPY src/DmOrder.Api/DmOrder.Api.csproj src/DmOrder.Api/

RUN dotnet restore src/DmOrder.Api/DmOrder.Api.csproj

COPY src/ src/

# No --no-restore gap: restore already ran, and publish reuses it from the layer above.
RUN dotnet publish src/DmOrder.Api/DmOrder.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp needs these.
#
# The managed package carries libSkiaSharp, but that binary links against fontconfig and freetype,
# which the aspnet image does not ship. Without them the first image upload fails with a
# DllNotFoundException - at runtime, on the first seller who uploads a photo, not at build time.
RUN apt-get update \
    && apt-get install --no-install-recommends -y libfontconfig1 libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

# A non-root user. If the process is ever compromised it should not also own the filesystem.
RUN useradd --create-home --shell /usr/sbin/nologin --uid 10001 ordviz
USER ordviz

COPY --from=build --chown=ordviz:ordviz /app/publish .

# A default for hosts that do not inject PORT. Render, Railway and Cloud Run all do, and Program.cs
# prefers theirs - see the PORT handling there.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Quieter startup logs and no diagnostic pipe we do not use.
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production

# Liveness only. /health is the shallow check that answers as long as the process is up;
# /health/ready additionally checks the database, which is deliberately NOT what an orchestrator
# restarts on - a database blip should not kill a healthy API.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "DmOrder.Api.dll"]
