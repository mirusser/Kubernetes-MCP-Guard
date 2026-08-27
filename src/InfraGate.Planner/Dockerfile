FROM alpine:3.21 AS filter
WORKDIR /src
COPY src/ src/
RUN mkdir /out && \
    find src -name '*.csproj' | tar -cf - -T - | tar -xf - -C /out

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY .editorconfig .
COPY --from=filter /out/src/ ./src/
RUN dotnet restore src/InfraGate.Planner/InfraGate.Planner.csproj

COPY src/ src/
RUN dotnet publish src/InfraGate.Planner/InfraGate.Planner.csproj \
    --configuration Release \
    --output /app/planner \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/planner
COPY --from=build /app/planner .
ENV InfraGate__Runtime__Environment=Production
ENV InfraGate__Planner__AspNetCoreUrls=http://+:3004
EXPOSE 3004
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.Planner.dll"]
