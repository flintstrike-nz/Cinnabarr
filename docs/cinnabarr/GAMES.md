# Games: library, metadata and play (design)

Roadmap items 5 and 6: manage emulator game libraries (ROMs) the way Cinnabarr manages shows and movies, then play them in the browser, with the goal of playing classic games from the Jellyfin app on an LG webOS TV with a controller.

Status: **design only. Three decisions and two keys are needed from the owner before any code (section 6).**

## 1. What it is

A third library next to Series and Movies: **Games**. A game belongs to a **platform** (NES, SNES, Mega Drive, Game Boy, GBA, PlayStation and so on). The user points Cinnabarr at ROM folders; it identifies each ROM, fetches cover art, plot and gameplay data, and lists it in the library. Everything built for the other libraries is reused: per-library root folders (#137), the Library page and its poster grid (#141), Doctor duplicate checks (#138-#140), drive balancing (#143) and the mobile view (#142).

## 2. Phases

| Phase | What | Needs |
| --- | --- | --- |
| G1 | Platform list, `Games` library type, root folders for it, a scan that finds ROM files and groups multi-file games (cue/bin, m3u, zipped sets) | nothing external |
| G2 | Identify by hash (No-Intro / Redump DAT files) and by filename; metadata and artwork from a provider; poster grid, details page, mobile view | a metadata provider key (section 4) |
| G3 | Play in the browser from the details page, with gamepad support, saves and save states stored on the server | emulator choice (section 3) |
| G4 | Play from the Jellyfin app on LG webOS | a prototype on a real TV (section 5) |
| G5 | Doctor for games (duplicate regions/revisions, bad dumps), storage balancing for the Games library | G1 |

Acquiring ROMs is out of scope. Cinnabarr does not search for, download or link to ROMs. The games library only manages files the user already has, and the legal notice shown before the engine is used is extended to say so.

## 3. Playing in the browser

Use **EmulatorJS** (RetroArch cores compiled to WebAssembly, runs in any modern browser, has gamepad support, save states, and an embeddable player).

Licence notes (to be confirmed by reading the files, not from summaries): EmulatorJS itself is GPL-3.0 ([README](https://cdn.jsdelivr.net/gh/EmulatorJS/EmulatorJS@main/README.md)). The cores are upstream libretro projects with their own licences, listed per core in [cores.json](https://cdn.emulatorjs.org/4.3.0-pre/data/cores/cores.json). Some cores are non-commercial or non-GPL. Cinnabarr's own licence is GPL-3.0 (Sonarr-derived), so shipping GPL cores is compatible; shipping non-commercial cores would not be if Cinnabarr were ever distributed commercially. **Plan: ship only cores whose licence file permits redistribution under GPL-3.0, and download the rest, if the user wants them, from the EmulatorJS CDN at their own choice.** Cinnabarr never bundles BIOS files.

The player is a page served by Cinnabarr (`/play/<gameId>`): ROM streamed from the server, saves and states posted back to the server so progress follows the user between devices.

## 4. Metadata providers

| Provider | Gives | Key needed |
| --- | --- | --- |
| [IGDB](https://docs.romm.app/5.1.0/getting-started/metadata-providers/) (via a Twitch app) | plot, genres, release dates, ratings, screenshots, covers | a free Twitch application (client id and secret) |
| ScreenScraper | covers, box art, screenshots, video snaps, hash matching | a free account; developer credentials from its forum for a higher rate |
| No-Intro / Redump DATs | exact name by file hash | none (public files) |

Plan: hash-match against DAT files first, then ask the provider(s) for the rest. Cover art is cached on the server. Whether ScreenScraper and IGDB allow caching and showing their art in a self-hosted app has to be checked against their terms before G2 ships.

## 5. Jellyfin on LG webOS (the end goal)

What is known: the official Jellyfin webOS app is a thin wrapper around the web interface the Jellyfin server serves ([jellyfin-webos](https://github.com/jellyfin/jellyfin-webos)). Whether that web client reads a gamepad, and whether extra pages can be added to it, was not confirmed.

Two possible routes:
1. **A Jellyfin plugin** (C#, runs in Jellyfin) that adds a Games library type and a page that embeds the Cinnabarr player. Works in the stock app if the webOS web view runs it. Needs a plugin to be written and kept up to date with Jellyfin releases.
2. **Cinnabarr's own page opened from the TV** (a web app on the TV's browser or the webOS sideload route). Simpler, does not need Jellyfin at all.

Risks: older webOS versions ship an old Chromium (WebAssembly speed, missing features); a TV gamepad must pair over Bluetooth and appear in the Gamepad API; the Jellyfin app's remote-control focus handling may fight the emulator for the d-pad. **G4 starts with a one-day prototype on a real TV** (EmulatorJS page, one 8-bit core, one controller) to see which of these is true.

## 6. Decisions needed from the owner

1. **Metadata provider:** IGDB, ScreenScraper, or both? Each needs a key the owner registers (Twitch application for IGDB; screenscraper.fr account). Cinnabarr cannot create these.
2. **Emulator licence stance:** OK to ship only GPL-compatible cores and fetch others on request?
3. **Which platforms first:** a short list (for example NES, SNES, Mega Drive, Game Boy/GBA, PlayStation) so cores and DATs can be chosen.
4. **LG TV:** model/webOS version and which controller, for the G4 prototype.
