FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/InfraGate.DevIssuer/InfraGate.DevIssuer.csproj src/InfraGate.DevIssuer/
RUN dotnet restore src/InfraGate.DevIssuer/InfraGate.DevIssuer.csproj

COPY src/InfraGate.DevIssuer/ src/InfraGate.DevIssuer/
RUN dotnet publish src/InfraGate.DevIssuer/InfraGate.DevIssuer.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.DevIssuer.dll"]
