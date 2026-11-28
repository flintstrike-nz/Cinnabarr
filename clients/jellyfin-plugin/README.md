# Cinnabarr plugin for Jellyfin

Lets people use Cinnabarr from the Jellyfin apps (the goal is the LG webOS app): browse and request shows and movies, manage downloads, run interactive search and play games. The design and the steps are in [docs/cinnabarr/JELLYFIN.md](../../docs/cinnabarr/JELLYFIN.md).

**Status: J5.** It holds the Cinnabarr address and API key, decides what each Jellyfin user may do, tests the connection, has the *Cinnabarr TV check* page, exposes the endpoints below, and adds the Cinnabarr screens to the Jellyfin app: Library, Discover and Search with requesting, and for managers Downloads and interactive search. Games come next.

## Using it

Users with a role see a Cinnabarr button (a "C") in Jellyfin's top bar, next to search. It opens a full-screen view:

- **Library**: everything in Cinnabarr, filtered to series, movies, or what is still requested.
- **Discover**: trending and popular movies (there is no list source for series yet).
- **Search**: find a movie or series by name.

Pick a poster to see its details and, if it is not in the library yet, **Request** it. Cinnabarr adds it with its default root folder and quality profile and starts searching.

Managers also get:

- **Downloads**: the queue, refreshed every 2 seconds, with progress, time left and any problem Cinnabarr reports. **Pause** / **Resume** (built-in engine downloads), **Grab now** (a release waiting out a delay), **Remove**, and **Remove and find another** (blocklists the release and searches again). Removing needs a second press within 4 seconds.
- **Find releases** on anything in the library: interactive search across indexers, in Cinnabarr's order, with quality, size, seeders, indexer, age and why a release is not wanted. **Grab** sends it to downloads; a release Cinnabarr would not pick needs a second press (**Grab anyway**). For a series, pick the season first (the newest is chosen). Grabs go to the main version; choosing a version slot comes later.

Remote: arrows move, OK selects, Back goes back (and closes the view). Controller: d-pad or left stick move, A selects, B goes back. While the view is open, Jellyfin's own handling of the remote and controller is held back.

### How the screens get into the app

The plugin adds one stylesheet and one script tag to jellyfin-web's `index.html` as Jellyfin serves it (an ASP.NET startup filter; no files are changed, so Docker and read-only installs work, and uninstalling removes it). The LG webOS app loads the page from the server, so it gets the screens too. Nothing else is needed: no File Transformation or JavaScript Injector plugin. The script finds Jellyfin's top bar by position (it works with both the newer app bar and the older header); if a Jellyfin update moves things around, that is the one place to adjust (`Web/client.js`, `headerSlot`).

## Build

Needs the .NET SDK from the repository's `global.json` (10.x) and `zip`.

```bash
./build.sh
```

```bash
./build.sh 10.11
```

The first builds for Jellyfin 12.x, the second for Jellyfin 10.11. Each writes `out/cinnabarr_<version>_jf<target>.zip` and `out/manifest.json` (the manifest describes the last build).

Check that the page scripts still run on the TV's old web engine (from the repository root, after `yarn install`):

```bash
node clients/jellyfin-plugin/check-es2019.cjs
```

Tests:

```bash
dotnet test clients/jellyfin-plugin/tests/Jellyfin.Plugin.Cinnabarr.Tests
```

## Install

Either:

- **By hand:** unzip the zip into `<Jellyfin data folder>/plugins/Cinnabarr_0.1.0.0/` and restart Jellyfin. In Docker that is usually `/config/plugins/` inside the container.
- **From a repository:** put `manifest.json` and the zip somewhere Jellyfin can download from. Build with `CINNABARR_PLUGIN_BASE_URL=<that address> ./build.sh` so the manifest points at the zip. In Jellyfin, go to *Dashboard → Plugins → Repositories*, add the manifest's address, then install *Cinnabarr* from the catalogue.

## Set up

1. *Dashboard → Plugins → Cinnabarr*. Enter the address Jellyfin uses to reach Cinnabarr (for example `http://localhost:8989`) and Cinnabarr's API key (*Settings → General → Security* in Cinnabarr), then press **Test connection**.
2. Tick what each user may do. Administrators can do everything; anyone not ticked sees nothing from Cinnabarr.
   - **Request:** browse, search and request.
   - **Manage:** also downloads, interactive search and deleting.
   - **Play:** games.
3. Save.

The API key is stored only in the plugin's settings on the Jellyfin server, which only administrators can read. The apps only ever call this plugin's `/Cinnabarr` endpoints.

## TV check

On the TV, open the Jellyfin app, sign in as an administrator, and go to *Dashboard → Cinnabarr TV check*. Pair the controller and press a few buttons on it and on the remote (including Back). The page answers the questions in JELLYFIN.md section 6: whether the controller is seen, whether streaming (WebRTC with H.264) can work, whether Jellyfin's own controller handling is on, and which remote keys arrive. **Copy report** puts the answers on the clipboard; they are also printed at the bottom of the page.

## Endpoints

All under `/Cinnabarr`, all need a signed-in Jellyfin user. This is a fixed list: nothing forwards a path the app chose, ids and names are checked before they reach Cinnabarr, and only Cinnabarr's `/api/v5` is ever called. Answers from Cinnabarr are passed on as they are (camelCase JSON, images and files streamed); the plugin's own answers are `{ "message": ... }` for errors.

| Call | Role | Cinnabarr call |
| --- | --- | --- |
| `GET Me` | any | (plugin) the caller's roles and whether Cinnabarr is set up |
| `POST Test` | administrator | (plugin) test `{ Url, ApiKey }` or the saved settings |
| `GET Series`, `GET Series/{id}` | Requester | `series`, `series/{id}` |
| `GET Movies`, `GET Movies/{id}` | Requester | `movie`, `movie/{id}` |
| `GET Image/Series/{id}/{file}` | Requester | `mediacover/{id}/{file}` (poster, fanart…; movies use their remote image URLs) |
| `GET Lookup/Series?term=`, `GET Lookup/Movies?term=` | Requester | `series/lookup`, `movie/lookup` |
| `GET Discover/Movies?list=trending\|popular` | Requester | `movie/lookup/discover` |
| `GET Status?tvdbIds=&tmdbIds=` | Requester | `request/status` |
| `POST Request` `{ mediaType, externalId, qualityProfileId?, searchNow?, seriesMonitor? }` | Requester | `request`, and records who asked |
| `GET Requests?count=` | Requester | (plugin) the request log: everyone's for a Manager, else the caller's own |
| `GET Queue?page=&pageSize=` | Manager | `queue` |
| `DELETE Queue/{id}?removalMethod=&blocklistMethod=` | Manager | `queue/{id}` |
| `POST Queue/{id}/Grab` | Manager | `queue/grab/{id}` |
| `POST Downloads/{downloadId}/Pause`, `…/Resume` | Manager | `engine/downloads/{downloadId}/pause`, `…/resume` |
| `GET Releases/Series?seriesId=&seasonNumber=&episodeId=&versionSlotId=` | Manager | `release` (interactive search) |
| `POST Releases/Series` | Manager | `release` (grab) |
| `GET Releases/Movie?movieId=&versionSlotId=`, `POST Releases/Movie` | Manager | `release/movie` |
| `GET Search?query=`, `POST Search/Grab` | Manager | `search`, `search/grab` |
| `GET Games`, `GET Games/{id}` | Player | `game`, `game/{id}` |
| `GET Games/{id}/Cover`, `GET Games/{id}/Play` | Player | `game/{id}/cover`, `game/{id}/play` |
| `GET Games/{id}/Rom` | Player | `game/{id}/rom`, with `Range` passed on |
| `GET`/`PUT Games/{id}/State` | Player | `game/{id}/state` (up to 64 MB) |
| `GET Games/Bios/{platformKey}` | Player | `game/bios/{platformKey}` |

The request log is `requests.jsonl` in the plugin's data folder, trimmed to the newest 2000 entries.

## Licence

GPL-3.0, like Cinnabarr. The Jellyfin packages it builds against (`Jellyfin.Controller`, `Jellyfin.Model`) are GPL-3.0-only.
