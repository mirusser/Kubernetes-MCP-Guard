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
RUN dotnet restore src/InfraGate.Executor/InfraGate.Executor.csproj

COPY src/ src/
RUN dotnet publish src/InfraGate.Executor/InfraGate.Executor.csproj \
    --configuration Release \
    --output /app/executor \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/executor
COPY --from=build /app/executor .
ENV InfraGate__Runtime__Environment=Production
ENV InfraGate__Executor__AspNetCoreUrls=http://+:3005
EXPOSE 3005
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.Executor.dll"]
