FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .
COPY .editorconfig .
COPY src/InfraGate.Remediation.Contracts/InfraGate.Remediation.Contracts.csproj src/InfraGate.Remediation.Contracts/
COPY src/InfraGate.ClientCredentials/InfraGate.ClientCredentials.csproj src/InfraGate.ClientCredentials/
COPY src/InfraGate.Observability/InfraGate.Observability.csproj src/InfraGate.Observability/
COPY src/InfraGate.RuntimeSafety/InfraGate.RuntimeSafety.csproj src/InfraGate.RuntimeSafety/
COPY src/InfraGate.Executor/InfraGate.Executor.csproj src/InfraGate.Executor/
RUN dotnet restore src/InfraGate.Executor/InfraGate.Executor.csproj

COPY src/InfraGate.Remediation.Contracts/ src/InfraGate.Remediation.Contracts/
COPY src/InfraGate.ClientCredentials/ src/InfraGate.ClientCredentials/
COPY src/InfraGate.Observability/ src/InfraGate.Observability/
COPY src/InfraGate.RuntimeSafety/ src/InfraGate.RuntimeSafety/
COPY src/InfraGate.Executor/ src/InfraGate.Executor/
RUN dotnet publish src/InfraGate.Executor/InfraGate.Executor.csproj \
    --configuration Release \
    --output /app/executor \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/executor
COPY --from=build /app/executor .
ENV INFRA_GATE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:3005
EXPOSE 3005
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.Executor.dll"]
