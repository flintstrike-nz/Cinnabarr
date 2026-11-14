# syntax=docker/dockerfile:1
#
# Cinnabarr in one image: the web app (.NET) and its built-in download engine (cinnabarr-engine), which the app starts
# and supervises itself. Build it from the repository root:
#
#   docker build -t cinnabarr .
#
# The engine is built from its own repository; choose another branch or tag with --build-arg ENGINE_REF=...

ARG ENGINE_REPO=https://github.com/flintstrike-nz/EmbarrPre3.git
ARG ENGINE_REF=master

# --- The web interface ---------------------------------------------------------------------------------------------
FROM node:24-slim AS frontend
WORKDIR /src
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile --network-timeout 600000
COPY tsconfig.json vite.config.ts index.html ./
COPY frontend ./frontend
COPY src/NzbDrone.Core/Localization ./src/NzbDrone.Core/Localization
RUN yarn build

# --- The application -----------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS app
WORKDIR /src
COPY . .
# global.json pins one exact SDK patch; the image's SDK is the newest one.
RUN rm -f global.json \
 && dotnet build src/Sonarr.sln -c Release --nologo -v q \
 && ls _output/net10.0/Sonarr.dll

# --- The engine ----------------------------------------------------------------------------------------------------
FROM debian:trixie-slim AS engine
ARG ENGINE_REPO
ARG ENGINE_REF
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates git build-essential cmake ninja-build pkg-config \
      qt6-base-dev qt6-base-private-dev qt6-tools-dev qt6-l10n-tools libqt6sql6-sqlite libtorrent-rasterbar-dev libboost-dev libssl-dev zlib1g-dev \
 && rm -rf /var/lib/apt/lists/*
RUN git clone --depth 1 --branch "${ENGINE_REF}" "${ENGINE_REPO}" /engine-src
RUN cmake -S /engine-src -B /engine-build -G Ninja -DCMAKE_BUILD_TYPE=Release \
      -DGUI=OFF -DWEBUI=ON -DCINNABARR_ENGINE=ON -DSYSTEMD=OFF -DTESTING=OFF \
 && cmake --build /engine-build -j"$(nproc)" \
 && strip /engine-build/cinnabarr-engine

# --- What runs -----------------------------------------------------------------------------------------------------
# The .NET runtime comes from Microsoft's image, but the final image is Debian 13 (not that image's Ubuntu), because the
# engine is built on Debian 13 and needs the same Qt and libtorrent versions at run time.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnet-runtime

FROM debian:trixie-slim AS runtime

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl ffmpeg tzdata util-linux passwd zlib1g libstdc++6 libicu76 \
      libqt6core6t64 libqt6network6 libqt6sql6 libqt6xml6 libqt6sql6-sqlite libtorrent-rasterbar2.0t64 libssl3t64 \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --gid 1000 cinnabarr \
 && useradd --uid 1000 --gid cinnabarr --no-create-home --shell /usr/sbin/nologin cinnabarr \
 && mkdir -p /config /app

COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

COPY --from=app /src/_output/net10.0 /app/bin
COPY --from=frontend /src/_output/UI /app/UI
COPY --from=engine /engine-build/cinnabarr-engine /app/engine/cinnabarr-engine
COPY distribution/docker/entrypoint.sh /entrypoint.sh

# The app reads media info with ffprobe from its own folder.
RUN ln -sf /usr/bin/ffprobe /app/bin/ffprobe \
 && chmod +x /entrypoint.sh /app/engine/cinnabarr-engine

# The commit this image was built from, so the app can tell you when a newer one exists.
ARG CINNABARR_COMMIT=unknown

ENV CINNABARR_COMMIT=$CINNABARR_COMMIT \
    CINNABARR_ENGINE_PATH=/app/engine/cinnabarr-engine \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0 \
    PUID=1000 \
    PGID=1000

# The web interface, and the engine's torrent port (Settings → Download Clients can change it).
EXPOSE 8989 51413/tcp 51413/udp
VOLUME /config

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -fsS http://localhost:8989/ping || exit 1

ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "/app/bin/Sonarr.dll", "--nobrowser", "--data=/config"]
