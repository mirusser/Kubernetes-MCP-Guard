FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/InfraGate.McpGateway.Auth/InfraGate.McpGateway.Auth.csproj src/InfraGate.McpGateway.Auth/
COPY src/InfraGate.McpGateway/InfraGate.McpGateway.csproj src/InfraGate.McpGateway/
COPY src/InfraGate.McpServer/InfraGate.McpServer.csproj src/InfraGate.McpServer/
RUN dotnet restore src/InfraGate.McpGateway/InfraGate.McpGateway.csproj
RUN dotnet restore src/InfraGate.McpServer/InfraGate.McpServer.csproj

COPY src/InfraGate.McpGateway.Auth/ src/InfraGate.McpGateway.Auth/
COPY src/InfraGate.McpGateway/ src/InfraGate.McpGateway/
COPY src/InfraGate.McpServer/ src/InfraGate.McpServer/
RUN dotnet publish src/InfraGate.McpServer/InfraGate.McpServer.csproj \
    --configuration Release \
    --output /app/server \
    --no-restore
RUN dotnet publish src/InfraGate.McpGateway/InfraGate.McpGateway.csproj \
    --configuration Release \
    --output /app/gateway \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app/gateway
COPY --from=build /app/gateway .
COPY --from=build /app/server /app/server
RUN install -d -o $APP_UID -g $APP_UID /data/approvals /data/guardrails
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.McpGateway.dll"]
