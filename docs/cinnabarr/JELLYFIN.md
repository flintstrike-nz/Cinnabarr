# Cinnabarr in Jellyfin on LG webOS, and streamed games (design)

Status: **scheduled; J0 (this document and the TV checklist) and J1 (the request and status API, section 8) are done.** This turns [Addendum A](ROADMAP.md#addendum-a--jellyfin--webos-integration-future-not-part-of-the-current-plan) into a plan and extends the games work in [GAMES.md](GAMES.md) with server-side emulation for the consoles a TV cannot emulate itself.

## 1. Goal

From the stock Jellyfin app on an LG webOS TV (the owner's TV is an OLED55A1, webOS 6, with an Xbox Elite Series 2 controller), and using only the remote or a controller:

- browse the Cinnabarr library (series and movies);
- search, discover and request new series and movies;
- manage downloads (the queue), and run interactive search and grab a release;
- play games for NES, SNES, Master System, Mega Drive, Nintendo 64, PlayStation, PlayStation 2, GameCube, Wii and Wii U.

## 2. Shape

```
LG TV: Jellyfin webOS app (stock, from the LG Content Store)
   │  loads jellyfin-web from the Jellyfin server; a script injected by the plugin adds the Cinnabarr and Games screens
   ▼
Jellyfin server + Cinnabarr plugin   /Cinnabarr/* endpoints: Jellyfin login + a role check
   │  calls a fixed list of Cinnabarr endpoints with the API key (the key never reaches the TV)
   ▼
Cinnabarr (/api/v5) ── PlaySupervisor ──▶ cinnabarr-play (127.0.0.1 control API + api.key)
                                            libretro core → offscreen GPU render → NVENC H.264
                                            ── WebRTC (H.264 + Opus video/audio, data channel for input) ──▶ TV
```

Three parts, each in this repository for now:

| Part | Where | Language |
| --- | --- | --- |
| Jellyfin plugin | `clients/jellyfin-plugin/` | C# (.NET), plain ES2019 JavaScript for the TV screens |
| Play engine `cinnabarr-play` | `engines/cinnabarr-play/` | C++ (CMake), libretro, FFmpeg, libdatachannel |
| Cinnabarr changes | `src/`, as usual | C# |

The plugin and the play engine can move to their own repositories later, so they can track Jellyfin and core releases on their own schedule.

## 3. Why our own libretro host and not RetroArch

The emulators RetroArch runs are **libretro cores**: separate libraries that all implement the same small API (about 30 callbacks for video, audio, input and save data). RetroArch itself is a desktop frontend around them. It expects a display, and its built-in streaming goes through FFmpeg to RTMP/UDP with seconds of delay, which is too slow to play.

So Cinnabarr does the same as it did with qBittorrent, but with less to carry: a small headless host, `cinnabarr-play`, loads the **same cores**, renders offscreen on the GPU, encodes with the GPU's encoder and streams to the TV with WebRTC. `EngineSupervisor` already shows how Cinnabarr starts, health-checks and restarts a sidecar; `PlaySupervisor` follows it.

Wii U is the exception: there is no libretro core, only the standalone Cemu emulator. It comes last, as an experimental capture mode (section 5, G8).

## 4. Play tiers

| Platform | Tier | Emulator |
| --- | --- | --- |
| NES, SNES, Master System, Mega Drive | **Browser**: runs on the TV (already built for SMS/SNES in G3) | EmulatorJS cores (nes, snes, segaMS, segaMD) |
| PlayStation, Nintendo 64 | **Auto**: browser if the TV keeps up, else stream | psx / n64 in the browser; swanstation or beetle_psx_hw, mupen64plus_next on the server |
| PlayStation 2 | **Stream** | LRPS2 (libretro PCSX2) |
| GameCube, Wii | **Stream** | dolphin_libretro |
| Wii U | **Stream, experimental** | standalone Cemu, captured |

Server: Linux/Docker with an NVIDIA GPU (NVENC for encoding, EGL/OpenGL or Vulkan for the cores). One streamed session at a time.

Cores are never bundled. As in GAMES.md, they are downloaded from the libretro buildbot only when the owner asks for them. BIOS files (PlayStation, PlayStation 2) and Wii U keys are supplied by the owner. Acquiring ROMs, BIOS files or keys stays out of scope.

## 5. Steps (one pull request each)

| Step | What |
| --- | --- |
| **J0** | This document, updates to Addendum A, GAMES.md and TV.md, and the TV checklist (section 6). |
| **J1** | Cinnabarr API gaps for clients: discover lists (trending, popular, upcoming), an "in library / requested" lookup by TVDB/TMDb id, and the single "add item with options" contract shared with Addendum B. |
| **J2** | Plugin skeleton: settings (Cinnabarr URL, API key, which Jellyfin users get which role), a *Test connection* button, a build script, a plugin repository `manifest.json`, and a probe page for the TV checklist. Targets Jellyfin 12.x (.NET 10, current stable 12.4), with a 10.11 (.NET 9) build option. |
| **J3** | Proxy endpoints under `/Cinnabarr`: a fixed list, no pass-through of arbitrary paths. Role checks and a per-user request log. |
| **J4** | Script injection (through the *File Transformation* plugin) and the TV screens: Library, Discover, Search and request. Spatial navigation taken from `frontend/src/tv.html`; the remote's Back key handled. |
| **J5** | Downloads (queue: progress, pause, resume, remove, blocklist, re-search) and interactive search with grab into a version slot. Manager role only. |
| **G6** | Platforms: NES, Mega Drive, Nintendo 64, GameCube, Wii, Wii U in the platform catalog, with GameCube and Wii told apart by the disc header. |
| **J6** | Games screen and browser-tier play inside Jellyfin. ROMs, saves and BIOS go through the plugin. |
| **G7a** | `cinnabarr-play` prototype with a software core (snes9x) and WebRTC. **Go/no-go gate:** measure the delay on the real TV. Continue if it stays under about 80 ms on the home network; if the Jellyfin app has no WebRTC, fall back to WebSocket + Media Source Extensions and accept more delay. |
| **G7b** | `PlaySupervisor`, a health check, core downloads and the `games/{id}/stream` session API in Cinnabarr. Saves go through `GamePlayService`. |
| **G7c** | GPU cores on headless EGL/NVIDIA: mupen64plus_next, swanstation, dolphin_libretro, LRPS2. |
| **G7d** | The streamed player in the plugin: video, controller input over the data channel, connection stats. |
| **G7e** | Docker: build stage, NVIDIA runtime, UDP ports for WebRTC; DOCKER.md and the user guide. |
| **G8** | Wii U (experimental): Cemu in a headless X/gamescope session, captured into the same stream, with a virtual controller through uinput. |

Order: J0 → J1 → J2 → J3 → J4 → J5 → G6 → J6 → G7a (gate) → G7b → G7c → G7d → G7e → G8.

### Plugin roles

Cinnabarr has one API key and no users, so the plugin decides who can do what, per Jellyfin user:

| Role | Can |
| --- | --- |
| Requester | browse, search, request |
| Manager | everything a Requester can, plus downloads, interactive search and deleting |
| Player | play games |

Jellyfin administrators have every role.

## 6. TV checklist (do this before J4 and G7a)

These are the questions the plan depends on. Each has to be answered on the real TV, **inside the Jellyfin app** (not the TV's web browser, which can behave differently). Until J2 ships the probe page, the first item can be checked with `/tv?debug=1` in the TV browser as described in [TV.md](TV.md).

| # | Question | Why it matters | If the answer is no |
| --- | --- | --- | --- |
| 1 | Does the Gamepad API list the controller (Bluetooth, then USB) inside the Jellyfin app? | Games cannot read a controller otherwise | Try USB; else games in Jellyfin are remote-only, and the `/tv` launcher stays the way to play |
| 2 | Does `RTCPeerConnection` exist, and does it receive H.264 video? | The streamed tier uses WebRTC | Fall back to WebSocket + Media Source Extensions (more delay) |
| 3 | Can jellyfin-web's own controller-to-arrow-keys handling be switched off while a game runs? | Otherwise the d-pad moves Jellyfin's focus during play | Hide Jellyfin's page while the game runs, or ask upstream for a switch |
| 4 | Does the remote's Back key (key code 461) reach the page, or does the app close? | Leaving screens and games | Use the Select + Start hold, as `/tv` does |
| 5 | Delay from button press to picture (G7a) | Decides whether streaming is playable | Lower the resolution, or keep heavy consoles library-only |

Record the answers (and the TV firmware and Jellyfin versions) in this section when they are known.

## 7. Risks

| Item | Detail |
| --- | --- |
| Plugin API drift | Jellyfin plugins are built against one server version; rebuild for each Jellyfin release. |
| Injection fragility | The injected script depends on jellyfin-web internals. Keep all selectors and hooks in one file. |
| Old web engine | webOS 6's web engine is Chromium 79-era: no WebCodecs, slower WebAssembly. The TV screens are plain ES2019 and checked as such, like `/tv`. |
| Network | WebRTC needs UDP between the server and the TV. In Docker that means a published UDP port range or host networking. Keep it on the home network; nothing is exposed to the internet. |
| GPU in Docker | Needs the NVIDIA container toolkit and `NVIDIA_DRIVER_CAPABILITIES=graphics,video,compute`. |
| Core quality | dolphin_libretro and LRPS2 lag behind the standalone Dolphin and PCSX2. If a core is not good enough, the capture mode from G8 can run the standalone emulator instead. |
| Licences | The cores have their own licences, some non-commercial; they are downloaded on request, never shipped (GAMES.md section 3). The Jellyfin server packages a plugin builds against (`Jellyfin.Controller`, `Jellyfin.Model`) are published as GPL-3.0-only, so the plugin is GPL-3.0, the same as Cinnabarr. |

## 8. The Cinnabarr API clients use (J1)

All under `/api/v5`, with the `X-Api-Key` header. Only the Jellyfin plugin (and later the request relay) holds the key.

| Call | What it does |
| --- | --- |
| `POST request` | Add a series or movie. Body: `{ "mediaType": "series" \| "movie", "externalId": <TVDB or TMDb id> }`, plus optional `qualityProfileId`, `rootFolderPath`, `searchNow` (default `true`), `seriesMonitor` (default `all`) and `tags`. Without them it uses the first root folder set aside for that library (else one that takes anything) and the oldest quality profile. Asking for something already in the library changes nothing and returns it with `alreadyExisted: true`, so a retry never makes a duplicate. |
| `GET request/status?tvdbIds=1,2&tmdbIds=3` | For each id: `mediaType`, `externalId`, the Cinnabarr `id` if it is in the library, and `status`: `notInLibrary`, `wanted`, `unmonitored`, `downloading`, `partial` (a series with some of its episodes) or `available`. At most 200 ids of each kind. |
| `GET movie/lookup/discover?list=trending\|popular` | Trending or popular movies (already existed). |
| `GET movie/lookup?term=`, `GET series/lookup?term=` | Search by name (already existed). |

There is no discover list for series: the series metadata source (SkyHook) has no trending or popular lists. The TV shows series search, and movie discover lists, until a source is chosen.
