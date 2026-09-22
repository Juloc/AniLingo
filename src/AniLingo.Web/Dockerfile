FROM debian:bookworm-slim AS codex
ARG CODEX_VERSION=0.155.1
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gzip tar \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /opt/codex/bin /opt/codex/home \
    && curl -fsSL https://chatgpt.com/codex/install.sh \
       | env CODEX_RELEASE="$CODEX_VERSION" \
             CODEX_NON_INTERACTIVE=1 \
             CODEX_INSTALL_DIR=/opt/codex/bin \
             CODEX_HOME=/opt/codex/home \
             sh \
    && cp -L /opt/codex/bin/codex /codex \
    && /codex --version

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AniLingo.Web/AniLingo.Web.csproj src/AniLingo.Web/
RUN dotnet restore src/AniLingo.Web/AniLingo.Web.csproj

COPY src/AniLingo.Web/ src/AniLingo.Web/
RUN dotnet publish src/AniLingo.Web/AniLingo.Web.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    CODEX_HOME=/data/codex
EXPOSE 8080
RUN mkdir -p /data/codex
COPY --from=codex /codex /usr/local/bin/codex
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AniLingo.Web.dll"]
