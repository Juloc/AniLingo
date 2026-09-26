FROM debian:bookworm-slim AS whisper
ARG WHISPER_CPP_VERSION=1.9.4
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential ca-certificates cmake curl tar \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /src/whisper \
    && curl -fsSL --retry 5 --retry-delay 2 --retry-all-errors \
       "https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v${WHISPER_CPP_VERSION}.tar.gz" \
       -o /tmp/whisper.tar.gz \
    && tar -xzf /tmp/whisper.tar.gz -C /src/whisper --strip-components=1 \
    && cmake -S /src/whisper -B /src/whisper/build \
       -DBUILD_SHARED_LIBS=OFF \
       -DWHISPER_BUILD_TESTS=OFF \
       -DWHISPER_BUILD_EXAMPLES=ON \
       -DGGML_NATIVE=OFF \
    && cmake --build /src/whisper/build --config Release --parallel 2 --target whisper-cli \
    && test -x /src/whisper/build/bin/whisper-cli

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

COPY design/player/ design/player/
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
    mkdir -p /data/codex /data/whisper /data/transcription-cache
COPY --from=codex /codex /usr/local/bin/codex
COPY --from=whisper /src/whisper/build/bin/whisper-cli /usr/local/bin/whisper-cli
COPY --from=japanese-data /var/lib/mecab/dic/open-jtalk/naist-jdic /var/lib/mecab/dic/open-jtalk/naist-jdic
COPY --from=japanese-data /dictionary ./dictionary
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AniLingo.Web.dll"]
