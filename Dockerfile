# aip-api: .NET 8 microservice for Mortgage Agreement in Principle (AIP)
# Targets:
#   runtime  (default)  small, non-root image deployed to EKS
#   coverage            runs the API under dotnet-coverage so E2E/API tests produce code coverage

# The SDK stage runs on the build machine's own CPU and cross-compiles for the target,
# so an Apple Silicon Mac can build linux/amd64 images for EKS without emulating the compiler.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/AipApi/AipApi.csproj src/AipApi/
RUN dotnet restore src/AipApi/AipApi.csproj -a $TARGETARCH
COPY src/ src/
RUN dotnet publish src/AipApi/AipApi.csproj -c Release -a $TARGETARCH -o /app --no-restore

# Coverage image: stop the container with SIGINT and the report is written to /coverage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS coverage
RUN dotnet tool install --global dotnet-coverage
ENV PATH="${PATH}:/root/.dotnet/tools" \
    ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
STOPSIGNAL SIGINT
ENTRYPOINT ["dotnet-coverage", "collect", "--output", "/coverage/aip-api.e2e.cobertura.xml", "--output-format", "cobertura", "dotnet AipApi.dll"]

# Runtime image (last stage, so it is the default build target)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AipApi.dll"]
