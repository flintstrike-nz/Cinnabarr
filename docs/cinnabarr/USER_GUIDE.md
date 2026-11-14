# Cinnabarr user guide

Cinnabarr is in early development, and some names inside it (the program file is still `Sonarr.dll`, the web address is still on port 8989) have not been changed yet. This guide describes how it works today.

## Running it

In Docker, see [DOCKER.md](DOCKER.md): one image holds the app and the engine. Without Docker:

1. Build or unpack Cinnabarr, and run `Sonarr.dll` with .NET 10: `dotnet Sonarr.dll --nobrowser --data=<folder>`. The data folder holds the database, settings, logs and the engine's own profile; give it a folder of its own.
2. The built-in download engine is a separate program, `cinnabarr-engine`. Put it next to the app, or set `CINNABARR_ENGINE_PATH` to its full path. Cinnabarr starts it, restarts it if it stops, and stops it on exit.
3. Open `http://<host>:8989`. On a fresh data folder you create a user name and password first.
4. The first time, a dialog shows the engine's legal notice and asks you to accept it. Nothing is downloaded until you do.

## First set-up

- **Settings → Media Management → Root Folders:** add the folders your series and movies live in. The app and the engine must see the same folders at the same paths.
  Each root folder has a **Library** setting (Any, Series or Movies). A series cannot be added or moved into a Movies root, and a movie cannot go into a Series root. Existing roots are assigned automatically on upgrade when they only hold one kind.
- **Settings → Media Management → Naming:** pick a preset: Jellyfin (the default), Emby or Plex. The presets differ only in how the provider ids are written in folder and file names (for example `[tvdbid-81189]`, `[tvdbid=81189]` or `{tvdb-81189}`).
- **Settings → Indexers:** add your Torznab indexers (or the trackers offered in the list). Use *Test* before saving. *Allow racing* is off by default; see below.
- **Settings → Profiles / Quality:** quality profiles decide what is wanted and what counts as an upgrade.
- **Settings → Download Clients:** the built-in engine is the only client. This page also holds the engine's port, speed limits (with an alternative-speed schedule), queue, privacy settings, seeding rules and the leftover-file review.

## Adding things

- **Library → Add New** (series) and **Add Movie** search the metadata services. Series metadata comes from Sonarr's services and movie metadata from Radarr's.
- **Library → Library Import / Import Movies** find folders already on disk and match them to titles.
- **Library → Import from Another Install** reads the data folder of an existing Sonarr, Radarr or Prowlarr and brings over series, movies or Torznab indexers, or takes over the torrents of a qBittorrent (point it at the profile folder that holds `BT_backup`; the torrents' folders must be reachable here, and they are checked and go on seeding). The other install is only read. If it saw its media at other paths (a container), fill in the two path boxes so `/tv/...` there becomes `/media/tv/...` here. Use *Preview* first. The files are found again by a scan, so the paths must be reachable. Episode-level monitoring, history, blocklist and custom formats are not brought over.
- **Discover** lists trending and popular movies; **Settings → Import Lists** keeps lists in sync.

## How downloads work

- Files download straight to their final place in the library under hidden temporary names, and are renamed into place when they pass the checks (only video and subtitle files are ever downloaded). There is no separate download folder.
- A pack's episodes appear one by one as each completes.
- Seeding is optional and set per series, season or episode, with a global default and a tracker's own minimum as a floor. When the goal is met the torrent is removed and the files stay.
- **Downloads** shows every torrent in the engine, including ones Cinnabarr did not add (shown as *unlinked*) and ones the engine lost (*missing from engine*, with *Re-add*). Open a download for its files, seeding goals, queue position and its own speed limits.

## Versions

A movie or a series can have several versions, such as HD and 4K, each with its own quality profile and its own file (for a series, one file for each episode).

- On a movie's or series' page, **Versions** adds, changes and removes versions. Each can be searched for on its own. For a series, **Search** looks for the version's missing episodes, and **Interactive Search** (with a season picker) lets you choose a release.
- **Settings → Media Management** has **Default Series Versions** and **Default Movie Versions**: every movie or series you add gets those versions.
- The automatic searches (RSS, Missing, Cutoff Unmet) cover every monitored version. A version of a series is missing an episode that is monitored, has aired and has no file for that version. **Wanted** lists these under **Series Versions**.
- In **Search**, a release can be grabbed for any version, or for a new one. The queue and history show the version after the title.
- Version names go into file names as ` - HD` (token `{Version}` in the file format).
- **Race mode:** *Settings → Download Clients → Race Sources per Movie Version* (1 is off) lets several releases download one movie version at once; the first one imported wins and the rest are stopped and removed without being blocklisted. Only releases from indexers that have *Allow racing* on take part, because stopping a download early can cost ratio on a private tracker.

## Keeping the library tidy

- **Organize** on a series or movie previews and applies renames, and moves a movie to the folder its naming format gives.
- **Settings → Download Clients → Leftover Partial Files** lists hidden partial files no torrent owns. Nothing is deleted until you approve the list.

## Where things are

- Logs: **System → Logs**. Health problems: **System → Status**.
- Backups: **System → Backup**.
- API: `/api/v5` (the old `/api/v3` is still there for other apps); the key is in **Settings → General**.

## Doctor

Doctor is the stethoscope button in the library (it replaces Reorganise).

- **Duplicates.** When an episode or movie has more than one file at the **same resolution**, Doctor flags the worse copy (lower source, then older revision, then lower video bitrate, then smaller file; differences under 10% don't count). Copies at different resolutions, like 4K next to 1080p, are treated as deliberate versions and left alone.
- **Review.** A red badge on the Doctor button counts flagged copies. The Doctor page shows each flagged copy next to the one it kept, with three screenshots of each. You can delete the flagged copy now, keep it instead (the other copy is flagged instead), or keep both.
- **Nightly check.** Doctor runs every 24 hours by default and deletes flagged copies after 7 days, using the recycle bin if you have one. Change the schedule, the retention, or switch it off on the Doctor page. Pressing *Check now* only looks and never deletes.
- **Reorganise.** Renaming files to the naming format, with undo, is still on the Doctor page.

## Storage (drive management)

Settings → Storage keeps the drives of a library evenly full.

1. In Settings → Media Management, set each root folder to **Series** or **Movies** (one root per drive).
2. Open Settings → Storage. It shows how full each drive is, what is downloading or due soon (striped), and what would be moved.
3. Tick *Balance the drives automatically* to run it every night (24 hours by default), or press *Move N item(s) now*.

How it decides: it compares free space as a percentage of each drive. If the emptiest and fullest drive differ by more than 10 points, it moves a whole series or movie from the fullest to the emptiest, picking the item that narrows the gap most, and repeats. Downloads in progress and the episodes and movies the calendar says are due in the next 14 days count as space already used. Items being downloaded are never moved, no drive is filled below the reserve (5% free), and one run moves at most 200 GB. It is off until you switch it on, because it moves your files.

A move updates the library path and tells Jellyfin / Emby (if connected in Settings → Connect with *Update Library* on) that the old folder is gone and the new one is there. New series and movies default to the drive with the most free space.
