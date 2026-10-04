# syntax=docker/dockerfile:1

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project files first so the restore layer is cached and a source-only
# change does not re-download the NuGet graph.
COPY LocalAiDocSummarizer.sln ./
COPY src/Core/Core.csproj                    src/Core/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj                      src/Api/
COPY tests/Core.Tests/Core.Tests.csproj      tests/Core.Tests/

RUN dotnet restore src/Api/Api.csproj

COPY src/ src/

RUN dotnet publish src/Api/Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

# A non-root user: the service parses untrusted uploads and has no need for
# privileges.
RUN useradd --create-home --shell /usr/sbin/nologin appuser

COPY --from=build /app/publish ./

USER appuser

EXPOSE 8080

# No HEALTHCHECK here: the aspnet image ships no curl or wget, and a command that
# always succeeds would report a healthy container that cannot actually summarize.
# docker-compose.yml polls /health instead, which genuinely reflects Ollama and
# model availability.

ENTRYPOINT ["dotnet", "Api.dll"]
