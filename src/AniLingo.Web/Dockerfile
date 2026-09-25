FROM debian:bookworm-slim AS whisper
# whisper.cpp publishes no checksum for its source archive. The pinned SHA-256 is
# the digest of the GitHub tag archive for v${WHISPER_CPP_VERSION}; update both
# values together when bumping the version.
ARG WHISPER_CPP_VERSION=1.9.4
ARG WHISPER_CPP_SHA256=57e280cee375ab02425b806ad5146b99f6eb9357e3c2b31357c8a6af2e2e44ae
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential ca-certificates cmake curl tar \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /src/whisper \
    && curl -fsSL --retry 5 --retry-delay 2 --retry-all-errors \
       "https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v${WHISPER_CPP_VERSION}.tar.gz" \
       -o /tmp/whisper.tar.gz \
    && { echo "${WHISPER_CPP_SHA256}  /tmp/whisper.tar.gz" | sha256sum -c - \
         || { echo "whisper.cpp v${WHISPER_CPP_VERSION} archive does not match the pinned SHA-256." >&2; exit 1; }; } \
    && tar -xzf /tmp/whisper.tar.gz -C /src/whisper --strip-components=1 \
    && cmake -S /src/whisper -B /src/whisper/build \
       -DBUILD_SHARED_LIBS=OFF \
       -DWHISPER_BUILD_TESTS=OFF \
       -DWHISPER_BUILD_EXAMPLES=ON \
       -DGGML_NATIVE=OFF \
    && cmake --build /src/whisper/build --config Release --parallel 2 --target whisper-cli \
    && test -x /src/whisper/build/bin/whisper-cli

FROM debian:bookworm-slim AS codex
# Pinned Codex CLI release package from https://github.com/openai/codex/releases.
# The SHA-256 values are the upstream entries of codex-package_SHA256SUMS for
# rust-v${CODEX_VERSION} (identical to the GitHub release asset digests); update
# them together with CODEX_VERSION. Only bin/codex is installed, as before.
ARG CODEX_VERSION=0.155.1
ARG CODEX_PACKAGE_SHA256_AMD64=a65b895c6ac1a73629bbe4b864640c86133e94a43b4d67b3103044e1a306d5a2
ARG CODEX_PACKAGE_SHA256_ARM64=71857dbc9bea3613410e8a69cfb46b07c0402d6d20fec18843dbaffd757634bd
ARG TARGETARCH
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gzip tar \
    && rm -rf /var/lib/apt/lists/*
RUN set -eu; \
    case "${TARGETARCH:-amd64}" in \
        amd64) codex_target=x86_64-unknown-linux-musl; codex_sha256="${CODEX_PACKAGE_SHA256_AMD64}" ;; \
        arm64) codex_target=aarch64-unknown-linux-musl; codex_sha256="${CODEX_PACKAGE_SHA256_ARM64}" ;; \
        *) echo "Unsupported target architecture for the Codex CLI: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    codex_asset="codex-package-${codex_target}.tar.gz"; \
    curl -fsSL --retry 5 --retry-delay 2 --retry-all-errors \
        "https://github.com/openai/codex/releases/download/rust-v${CODEX_VERSION}/${codex_asset}" \
        -o "/tmp/${codex_asset}"; \
    echo "${codex_sha256}  /tmp/${codex_asset}" | sha256sum -c - \
        || { echo "Codex ${CODEX_VERSION} ${codex_asset} does not match the pinned SHA-256." >&2; exit 1; }; \
    mkdir -p /tmp/codex-package; \
    tar -xzf "/tmp/${codex_asset}" -C /tmp/codex-package bin/codex; \
    install -m 0755 /tmp/codex-package/bin/codex /codex; \
    rm -rf /tmp/codex-package "/tmp/${codex_asset}"; \
    /codex --version

FROM debian:bookworm-slim AS japanese-data
ARG JMDICT_VERSION=3.6.2+20260921173324
ARG JMDICT_GER_URL=https://github.com/scriptin/jmdict-simplified/releases/download/3.6.2%2B20260921173324/jmdict-ger-3.6.2%2B20260921173324.json.tgz
ARG JMDICT_ENG_URL=https://github.com/scriptin/jmdict-simplified/releases/download/3.6.2%2B20260921173324/jmdict-eng-common-3.6.2%2B20260921173324.json.tgz
ARG JMDICT_GER_SHA256=3a6c2cb1580ea845d87689e0982664dd1edc4cbc84426bba03e913e8fa6f81fc
ARG JMDICT_ENG_SHA256=db98447ee0b3918ac29f58c95d0b5b0b1e2aeb15f81fbe13d53ee20427028424
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl jq tar open-jtalk-mecab-naist-jdic \
    && rm -rf /var/lib/apt/lists/*
RUN test -f /var/lib/mecab/dic/open-jtalk/naist-jdic/sys.dic
RUN mkdir -p /dictionary /tmp/jmdict-ger /tmp/jmdict-eng \
    && curl -fsSL --retry 5 --retry-delay 2 --retry-all-errors \
       "${JMDICT_GER_URL}" \
       -o /tmp/jmdict-ger.tgz \
    && curl -fsSL --retry 5 --retry-delay 2 --retry-all-errors \
       "${JMDICT_ENG_URL}" \
       -o /tmp/jmdict-eng.tgz \
    && echo "${JMDICT_GER_SHA256}  /tmp/jmdict-ger.tgz" | sha256sum -c - \
    && echo "${JMDICT_ENG_SHA256}  /tmp/jmdict-eng.tgz" | sha256sum -c - \
    && tar -xzf /tmp/jmdict-ger.tgz -C /tmp/jmdict-ger \
    && tar -xzf /tmp/jmdict-eng.tgz -C /tmp/jmdict-eng \
    && GER_JSON="$(find /tmp/jmdict-ger -type f -name '*.json' -print -quit)" \
    && ENG_JSON="$(find /tmp/jmdict-eng -type f -name '*.json' -print -quit)" \
    && test -n "$GER_JSON" \
    && test -n "$ENG_JSON" \
    && jq -r '.words[] | . as $w | ([.sense[].gloss[].text] | unique | .[0:3] | join("; ") | gsub("[\\t\\r\\n]+"; " ")) as $meaning | ($w.kana[0].text // "") as $reading | (($w.kanji[]? | [.text, $reading, (if .common then "1" else "0" end), $meaning] | @tsv), ($w.kana[]? | [.text, .text, (if .common then "1" else "0" end), $meaning] | @tsv))' "$GER_JSON" > /dictionary/jmdict-ger.tsv \
    && jq -r '.words[] | . as $w | ([.sense[].gloss[].text] | unique | .[0:3] | join("; ") | gsub("[\\t\\r\\n]+"; " ")) as $meaning | ($w.kana[0].text // "") as $reading | (($w.kanji[]? | [.text, $reading, (if .common then "1" else "0" end), $meaning] | @tsv), ($w.kana[]? | [.text, .text, (if .common then "1" else "0" end), $meaning] | @tsv))' "$ENG_JSON" > /dictionary/jmdict-eng-common.tsv \
    && test -s /dictionary/jmdict-ger.tsv \
    && test -s /dictionary/jmdict-eng-common.tsv

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
RUN set -eux; \
    for attempt in 1 2 3; do \
        rm -rf /var/lib/apt/lists/*; \
        apt-get update -o Acquire::Retries=5; \
        if apt-get install -y --no-install-recommends -o Acquire::Retries=5 ca-certificates curl ffmpeg libgomp1 libstdc++6; then \
            break; \
        fi; \
        if [ "$attempt" = "3" ]; then \
            exit 1; \
        fi; \
        sleep $((attempt * 3)); \
    done; \
    ffmpeg -version >/dev/null; \
    ffprobe -version >/dev/null; \
    rm -rf /var/lib/apt/lists/*; \
    mkdir -p /data/codex /data/whisper /data/transcription-cache; \
    chown -R "${APP_UID}:${APP_UID}" /data
# AniLingo runs as the aspnet image's non-root "app" user (APP_UID, 1654:1654).
# Only /data (persistent state) and /tmp (scratch) are writable for it; the
# application, tools and dictionaries stay root-owned and read-only.
# Data volumes created by earlier root-based images are root-owned. The
# entrypoint refuses to start until /data is writable and prints the one-time
# ownership fix documented in docs/ADMIN_OPERATIONS.md; it never chowns itself.
COPY --chmod=0755 <<"EOF" /usr/local/bin/anilingo-entrypoint
#!/bin/sh
set -eu

data_root=/data
runtime_uid="$(id -u)"
runtime_gid="$(id -g)"

fail_data_access() {
    cat >&2 <<MSG
AniLingo startup aborted: $1
AniLingo runs as the non-root user uid=${runtime_uid} gid=${runtime_gid} and needs read/write access to everything under ${data_root}.
Data volumes created by AniLingo images that still ran as root are owned by root and must be handed over once.
Stop AniLingo and run (replace <anilingo-data> with the data volume name or host path mounted at ${data_root}):

  docker run --rm --user 0:0 --entrypoint chown -v <anilingo-data>:/data ghcr.io/juloc/anilingo:latest -R ${runtime_uid}:${runtime_gid} /data

Then start AniLingo again. Read-only media mounts need no change.
See "Non-root container runtime and /data ownership" in docs/ADMIN_OPERATIONS.md.
MSG
    exit 1
}

if [ ! -d "${data_root}" ]; then
    fail_data_access "${data_root} does not exist or is not a directory."
fi

blocked_path="$(find "${data_root}" \
    \( -type d \( ! -readable -o ! -writable -o ! -executable \) \) -print -quit \
    -o \( -type f \( ! -readable -o ! -writable \) \) -print -quit \
    2>/dev/null || true)"
if [ -n "${blocked_path}" ]; then
    fail_data_access "${blocked_path} is not readable and writable by the AniLingo runtime user."
fi

exec dotnet /app/AniLingo.Web.dll "$@"
EOF
# Windows checkouts with core.autocrlf add CR to the heredoc; normalize and syntax-check.
RUN sed -i 's/\r$//' /usr/local/bin/anilingo-entrypoint \
    && sh -n /usr/local/bin/anilingo-entrypoint
COPY --from=codex /codex /usr/local/bin/codex
COPY --from=whisper /src/whisper/build/bin/whisper-cli /usr/local/bin/whisper-cli
COPY --from=japanese-data /var/lib/mecab/dic/open-jtalk/naist-jdic /var/lib/mecab/dic/open-jtalk/naist-jdic
COPY --from=japanese-data /dictionary ./dictionary
COPY --from=build /app/publish .
USER ${APP_UID}
ENTRYPOINT ["/usr/local/bin/anilingo-entrypoint"]
