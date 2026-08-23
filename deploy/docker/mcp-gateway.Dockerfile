FROM alpine:3.21 AS filter
WORKDIR /src
COPY src/ src/
RUN mkdir /out && \
    find src -name '*.csproj' | tar -cf - -T - | tar -xf - -C /out

# Secondary, read-only-only downstream MCP process (see docs/adr for the
# decision record). The official release binary is statically linked -- the
# chiseled runtime image below has no shell/libc for a dynamically-linked
# binary to link against. Version and per-platform SHA-256 are pinned once,
# in kubernetes-mcp-server.manifest.json; the installer verifies the
# checksum and the binary's own --version output before trusting it.
FROM alpine:3.21 AS k8s-mcp-build
RUN apk add --no-cache bash coreutils curl jq
COPY scripts/install-kubernetes-mcp-server.sh scripts/kubernetes-mcp-server.manifest.json /src/scripts/
RUN KUBERNETES_MCP_SERVER_INSTALL_DIR=/out /src/scripts/install-kubernetes-mcp-server.sh

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY .editorconfig .
COPY --from=filter /out/src/ ./src/
RUN dotnet restore src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
RUN dotnet restore src/InfraGate.McpServer/InfraGate.McpServer.csproj

COPY src/ src/
RUN dotnet publish src/InfraGate.McpServer/InfraGate.McpServer.csproj \
    --configuration Release \
    --output /app/server \
    --no-restore
RUN dotnet publish src/InfraGate.McpGateway/InfraGate.McpGateway.csproj \
    --configuration Release \
    --output /app/gateway \
    --no-restore
RUN install -d -o $APP_UID -g $APP_UID /data/approvals /data/guardrails /data/logs /data/dataprotection-keys

# Build a tiny health-check binary for the chiseled runtime image (no shell/curl/wget).
RUN mkdir -p /app/healthcheck && \
    cat > /app/healthcheck/Program.cs <<'EOF'
using System;
using System.Net.Http;
using System.Threading.Tasks;

string url = args.Length > 0 ? args[0] : "http://localhost:3001/healthz";
try
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var response = await client.GetAsync(url).ConfigureAwait(false);
    Environment.Exit(response.IsSuccessStatusCode ? 0 : 1);
}
catch
{
    Environment.Exit(1);
}
EOF
RUN dotnet new console -o /tmp/hc -n HealthCheck --force && \
    cp /app/healthcheck/Program.cs /tmp/hc/Program.cs && \
    dotnet publish /tmp/hc/HealthCheck.csproj \
        --configuration Release \
        --output /app/healthcheck \
        --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app/gateway
COPY --from=build /app/gateway .
COPY --from=build /app/server /app/server
COPY --from=build /app/healthcheck /app/healthcheck
COPY --from=build /data /data
COPY --from=k8s-mcp-build /out/kubernetes-mcp-server /app/k8s-mcp-server/kubernetes-mcp-server
COPY --from=k8s-mcp-build /out/kubernetes-mcp-server.manifest.json /app/k8s-mcp-server/kubernetes-mcp-server.manifest.json
ENV InfraGate__Runtime__Environment=Production
USER $APP_UID
HEALTHCHECK --interval=5s --timeout=5s --retries=10 --start-period=15s \
    CMD ["dotnet", "/app/healthcheck/HealthCheck.dll", "http://localhost:3001/readyz"]
ENTRYPOINT ["dotnet", "InfraGate.McpGateway.dll"]
