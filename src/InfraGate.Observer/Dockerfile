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
RUN dotnet restore src/InfraGate.Observer/InfraGate.Observer.csproj

COPY src/ src/
RUN dotnet publish src/InfraGate.Observer/InfraGate.Observer.csproj \
    --configuration Release \
    --output /app/observer \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/observer
COPY --from=build /app/observer .
ENV InfraGate__Runtime__Environment=Production
ENV InfraGate__Observer__AspNetCoreUrls=http://+:3003
EXPOSE 3003
USER $APP_UID
ENTRYPOINT ["dotnet", "InfraGate.Observer.dll"]
