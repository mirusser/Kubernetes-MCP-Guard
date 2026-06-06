# src/InfraGate.RfcRag/Dockerfile
# Multi-stage build: filter (extract csproj) → build (restore + publish) → runtime (alpine)
#
# Build:
#   docker build -t rfc-rag -f src/InfraGate.RfcRag/Dockerfile .
#
# Run (stdio MCP server — keeps running, listening on stdin):
#   docker run --rm -i --network host \
#     -v ~/rfc-mirror:/rfc-mirror:ro \
#     -e InfraGate__RfcRag__PostgresConnectionString="Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres" \
#     -e InfraGate__RfcRag__RfcMirrorPath=/rfc-mirror \
#     -e InfraGate__OpenRouter__ApiKey="sk-or-..." \
#     rfc-rag
#
# Re-index a specific RFC (runs then exits):
#   docker run --rm -i \
#     ...same env vars... \
#     rfc-rag --reindex=2119
#
# Force re-index all RFCs (runs then exits):
#   docker run --rm -i \
#     ...same env vars... \
#     rfc-rag --force

FROM alpine:3.21 AS filter
WORKDIR /src
COPY src/ src/
RUN mkdir /out && \
    find src -name '*.csproj' | tar -cf - -T - | tar -xf - -C /out

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .
COPY .editorconfig .
COPY --from=filter /out/src/ ./src/
RUN dotnet restore src/InfraGate.RfcRag/InfraGate.RfcRag.csproj

COPY src/ src/
RUN dotnet publish src/InfraGate.RfcRag/InfraGate.RfcRag.csproj \
    --configuration Release \
    --output /app/rfc-rag \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/rfc-rag
COPY --from=build /app/rfc-rag .

ENV \
    InfraGate__RfcRag__RunMigrationsOnStartup=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.RfcRag.dll"]
