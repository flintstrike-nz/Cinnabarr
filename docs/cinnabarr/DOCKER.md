# Running Cinnabarr in Docker

One image holds the web app and its built-in download engine (`cinnabarr-engine`). The app starts the engine, restarts it if it stops and stops it on exit, so there is only one container and nothing to link.

## Build and start

There is no published image yet, so you build it yourself (it takes a while the first time: it builds the web interface, the app and the engine).

```bash
git clone https://github.com/flintstrike-nz/Cinnabarr.git
cd Cinnabarr
# edit the two media paths in docker-compose.yml, then:
docker compose up -d --build
```

Then open `http://<your server>:8989`, create your user name and password, and accept the engine's legal notice when it asks.

Or without Compose:

```bash
docker build -t cinnabarr .
docker run -d --name cinnabarr --restart unless-stopped \
  -e PUID=1000 -e PGID=1000 -e TZ=Pacific/Auckland \
  -p 8989:8989 -p 51413:51413 -p 51413:51413/udp \
  -v /path/to/config:/config \
  -v /path/to/tv:/tv -v /path/to/movies:/movies \
  cinnabarr
```

To use a different engine version, build with `--build-arg ENGINE_REF=<branch or tag>` (the engine comes from `https://github.com/flintstrike-nz/EmbarrPre3`; change `ENGINE_REPO` to use another place).

## What to set

| Setting | What it does |
| --- | --- |
| `/config` volume | The database, settings, logs, backups and the engine's own profile (its torrent state). Keep it; it is everything that is Cinnabarr's. |
| Media volumes | Mount your library folders. Use the same paths in **Settings → Media Management → Root Folders** as you mount (for example `/tv` and `/movies`). The engine runs in the same container, so there is no download folder, no category and no path mapping to set up. |
| `PUID` / `PGID` | The user and group that own `/config` and that write to your media folders. Use the owner of your media (`id <user>` on the host). Default 1000. |
| `UMASK` | Optional, for example `002`. |
| `TZ` | Your time zone. |
| Port `8989` | The web interface. |
| Port `51413` (TCP and UDP) | The engine's torrent port. Forward it on your router if you want other peers to reach you. It can be changed in **Settings → Download Clients**; if you change it, publish the new port. |

If you start the container with `--user`, PUID and PGID are ignored and that user must own `/config` and be able to write to the media folders.

## Hard links and the same file system

Cinnabarr downloads straight into the library folder, so the library and the downloads are always on the same file system; there is nothing to keep together. Mount each library folder once.

## Updating

```bash
git pull
docker compose up -d --build
```

The database is upgraded by the new version when it starts. Back up `/config` first (or use **System → Backup**).

## Bringing your library over from Sonarr or Radarr

Mount the other install's config folder read-only into this container, for example `-v /path/to/sonarr-config:/import/sonarr:ro`, then use **Library → Import from Another Install** with `/import/sonarr` as the folder. If the other install saw its media at other paths, fill in the two path boxes so the paths line up with what you mounted here.

## Taking over your qBittorrent torrents

Mount the old qBittorrent's profile folder (the one with `BT_backup` in it) read-only, and the folder its torrents live in at the same path it used (or fill in the path boxes), then use **Library → Import from Another Install → qBittorrent**. Each torrent is added where its files are, checked, and goes on seeding.

## Indexers

On its first start Cinnabarr downloads the indexer definitions (about 580 sites) from `https://github.com/Prowlarr/Indexers` and then refreshes them daily; if that download fails, the copy shipped in the image (`/app/bin/Definitions`) is used. Add indexers in **Settings → Indexers → Add**. The source can be changed with `IndexerDefinitionsUrl` in `config.xml`. Indexers that Prowlarr implements in code rather than as definition files are not part of this set yet (see the roadmap).

## Checking that it works

- `docker logs cinnabarr` shows the app's start-up and the engine starting.
- **System → Status** lists problems (for example a root folder that can't be written to).
- The container's health check calls `/ping` on port 8989.
