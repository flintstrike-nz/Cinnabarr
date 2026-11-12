# Cinnabarr user guide

Cinnabarr is in early development, and some names inside it (the program file is still `Sonarr.dll`, the web address is still on port 8989) have not been changed yet. This guide describes how it works today.

## Running it

1. Build or unpack Cinnabarr, and run `Sonarr.dll` with .NET 10: `dotnet Sonarr.dll --nobrowser --data=<folder>`. The data folder holds the database, settings, logs and the engine's own profile; give it a folder of its own.
2. The built-in download engine is a separate program, `cinnabarr-engine`. Put it next to the app, or set `CINNABARR_ENGINE_PATH` to its full path. Cinnabarr starts it, restarts it if it stops, and stops it on exit.
3. Open `http://<host>:8989`. On a fresh data folder you create a user name and password first.
4. The first time, the engine asks you to accept its legal notice (**Downloads** page or **Settings → Download Clients**). Nothing is downloaded until you do.

## First set-up

- **Settings → Media Management → Root Folders:** add the folders your series and movies live in. The app and the engine must see the same folders at the same paths.
- **Settings → Media Management → Naming:** pick a preset: Jellyfin (the default), Emby or Plex. The presets differ only in how the provider ids are written in folder and file names (for example `[tvdbid-81189]`, `[tvdbid=81189]` or `{tvdb-81189}`).
- **Settings → Indexers:** add your Torznab indexers (or the trackers offered in the list). Use *Test* before saving. *Allow racing* is off by default; see below.
- **Settings → Profiles / Quality:** quality profiles decide what is wanted and what counts as an upgrade.
- **Settings → Download Clients:** the built-in engine is the only client. This page also holds the engine's port, speed limits (with an alternative-speed schedule), queue, privacy settings, seeding rules and the leftover-file review.

## Adding things

- **Library → Add New** (series) and **Add Movie** search the metadata services. Series metadata comes from Sonarr's services and movie metadata from Radarr's.
- **Library → Library Import / Import Movies** find folders already on disk and match them to titles.
- **Library → Import from Another Install** reads the data folder of an existing Sonarr, Radarr or Prowlarr and brings over series, movies or Torznab indexers. The other install is only read. If it saw its media at other paths (a container), fill in the two path boxes so `/tv/...` there becomes `/media/tv/...` here. Use *Preview* first. The files are found again by a scan, so the paths must be reachable. Episode-level monitoring, history, blocklist and custom formats are not brought over.
- **Discover** lists trending and popular movies; **Settings → Import Lists** keeps lists in sync.

## How downloads work

- Files download straight to their final place in the library under hidden temporary names, and are renamed into place when they pass the checks (only video and subtitle files are ever downloaded). There is no separate download folder.
- A pack's episodes appear one by one as each completes.
- Seeding is optional and set per series, season or episode, with a global default and a tracker's own minimum as a floor. When the goal is met the torrent is removed and the files stay.
- **Downloads** shows every torrent in the engine, including ones Cinnabarr did not add (shown as *unlinked*) and ones the engine lost (*missing from engine*, with *Re-add*). Open a download for its files, seeding goals, queue position and its own speed limits.

## Versions

A movie can have several versions, such as HD and 4K, each with its own quality profile and its own file.

- On a movie's page, **Versions** adds, changes and removes versions. Each can be searched for on its own (automatic or interactive).
- **Settings → Media Management → Default Versions** gives every movie you add a set of versions.
- In **Search**, a movie release can be grabbed for any version, or for a new one.
- Version names go into file names as ` - HD` (token `{Version}` in the movie file format).
- **Race mode:** *Settings → Download Clients → Race Sources per Movie Version* (1 is off) lets several releases download one movie version at once; the first one imported wins and the rest are stopped and removed without being blocklisted. Only releases from indexers that have *Allow racing* on take part, because stopping a download early can cost ratio on a private tracker.

## Keeping the library tidy

- **Organize** on a series or movie previews and applies renames, and moves a movie to the folder its naming format gives.
- **Settings → Download Clients → Leftover Partial Files** lists hidden partial files no torrent owns. Nothing is deleted until you approve the list.

## Where things are

- Logs: **System → Logs**. Health problems: **System → Status**.
- Backups: **System → Backup**.
- API: `/api/v5` (the old `/api/v3` is still there for other apps); the key is in **Settings → General**.
