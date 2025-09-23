# -----------------------
# Runtime image
# -----------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# -----------------------
# Build image
# -----------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY src/FfaasLite.Core/FfaasLite.Core.csproj           src/FfaasLite.Core/
COPY src/FfaasLite.Infrastructure/FfaasLite.Infrastructure.csproj src/FfaasLite.Infrastructure/
COPY src/FfaasLite.Api/FfaasLite.Api.csproj             src/FfaasLite.Api/

RUN dotnet restore src/FfaasLite.Api/FfaasLite.Api.csproj

COPY src/ ./src/

RUN dotnet build src/FfaasLite.Api/FfaasLite.Api.csproj -c $BUILD_CONFIGURATION -o /app/build

# -----------------------
# Publish image
# -----------------------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish src/FfaasLite.Api/FfaasLite.Api.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# -----------------------
# Final runtime
# -----------------------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FfaasLite.Api.dll"]
