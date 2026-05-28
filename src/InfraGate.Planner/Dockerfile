FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .
COPY .editorconfig .
COPY src/InfraGate.AgentLlm/InfraGate.AgentLlm.csproj src/InfraGate.AgentLlm/
COPY src/InfraGate.Observer.Contracts/InfraGate.Observer.Contracts.csproj src/InfraGate.Observer.Contracts/
COPY src/InfraGate.Remediation.Contracts/InfraGate.Remediation.Contracts.csproj src/InfraGate.Remediation.Contracts/
COPY src/InfraGate.ClientCredentials/InfraGate.ClientCredentials.csproj src/InfraGate.ClientCredentials/
COPY src/InfraGate.AuditOutbox/InfraGate.AuditOutbox.csproj src/InfraGate.AuditOutbox/
COPY src/InfraGate.AuditOutbox.Postgres/InfraGate.AuditOutbox.Postgres.csproj src/InfraGate.AuditOutbox.Postgres/
COPY src/InfraGate.Observability/InfraGate.Observability.csproj src/InfraGate.Observability/
COPY src/InfraGate.RuntimeSafety/InfraGate.RuntimeSafety.csproj src/InfraGate.RuntimeSafety/
COPY src/InfraGate.Planner/InfraGate.Planner.csproj src/InfraGate.Planner/
RUN dotnet restore src/InfraGate.Planner/InfraGate.Planner.csproj

COPY src/InfraGate.AgentLlm/ src/InfraGate.AgentLlm/
COPY src/InfraGate.Observer.Contracts/ src/InfraGate.Observer.Contracts/
COPY src/InfraGate.Remediation.Contracts/ src/InfraGate.Remediation.Contracts/
COPY src/InfraGate.ClientCredentials/ src/InfraGate.ClientCredentials/
COPY src/InfraGate.AuditOutbox/ src/InfraGate.AuditOutbox/
COPY src/InfraGate.AuditOutbox.Postgres/ src/InfraGate.AuditOutbox.Postgres/
COPY src/InfraGate.Observability/ src/InfraGate.Observability/
COPY src/InfraGate.RuntimeSafety/ src/InfraGate.RuntimeSafety/
COPY src/InfraGate.Planner/ src/InfraGate.Planner/
COPY src/Shared/ src/Shared/
RUN dotnet publish src/InfraGate.Planner/InfraGate.Planner.csproj \
    --configuration Release \
    --output /app/planner \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/planner
COPY --from=build /app/planner .
ENV INFRA_GATE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:3004
EXPOSE 3004
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.Planner.dll"]
