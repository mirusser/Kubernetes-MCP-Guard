FROM alpine:3.21 AS filter
WORKDIR /src
COPY src/ src/
RUN mkdir /out && \
    find src -name '*.csproj' | tar -cf - -T - | tar -xf - -C /out

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app/gateway
COPY --from=build /app/gateway .
COPY --from=build /app/server /app/server
COPY --from=build /data /data
ENV InfraGate__Runtime__Environment=Production
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.McpGateway.dll"]
