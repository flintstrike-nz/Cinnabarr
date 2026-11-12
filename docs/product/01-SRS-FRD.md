# Cinnabarr: Software Requirements Specification (SRS) and Functional Requirements Document (FRD)

| Item | Value |
| --- | --- |
| Document | SRS / FRD, draft v0.1 |
| Date | 2026-11-06 |
| Baseline | `origin/develop` at `8507a4c` (merged PRs #2 to #79) |
| Status | Draft for owner review. Not yet agreed. |
| Source of truth | `docs/cinnabarr/ROADMAP.md`, `AGENTS.md`, Addenda A to C |
| Companion documents | [PRD](03-PRD.md), [RTM](02-RTM.md), [Product backlog](04-product-backlog.md) |

## 1. Introduction

### 1.1 Purpose

This specification states what Cinnabarr must do, the constraints it works under, and the status of each requirement at the baseline above. It is the functional reference that the Requirements Traceability Matrix ties to roadmap sections, merged pull requests and verification. The product backlog delivers the work that is still open.

### 1.2 Scope

Cinnabarr is one self-hosted application, for the owner's use, that merges Sonarr (the base), Radarr, Prowlarr and a headless qBittorrent engine (ROADMAP header). In scope: one library of series and movies, search across torrent indexers, a built-in download engine, direct-to-final downloads with per-file checks, seeding rules, version slots and races, migration from existing installs, and packaging.

Out of scope: Usenet (CON-04), external download clients (CON-05), circumventing DRM (roadmap §8, Addendum C.9), and multi-user accounts (not in the current plan).

### 1.3 Definitions

Terms are in section 5.

### 1.4 References

- `docs/cinnabarr/ROADMAP.md` at the baseline, all sections including build notes for Phases 2, 3 and 4.
- `AGENTS.md` (working rules and checks).
- Addendum A (Jellyfin and webOS), Addendum B (Vercel relay) and Addendum C (web sources and yt-dlp).
- Merged pull requests on `develop`, #2 to #79, read from titles and merge history.
- `flintstrike-nz/NHKollector` (archived), the owner's earlier NHK World downloader.
- The Phase 4 notes on `develop` listing work done and work still to do, which set most open statuses.

### 1.5 Conventions

- **Shall** marks a mandatory requirement. Requirement IDs are grouped by prefix (LIB, ENG, FIL and so on).
- **Priority** uses MoSCoW: M must, S should, C could, W won't.
- **Status**: *Done* (built and merged); *Partial* (some built, the rest open or unconfirmed); *Open* (planned, not built); *Hold* (deliberately waiting); *Ongoing* (a standing rule); *Future* (in an addendum, not the current plan).
- **Phase** follows ROADMAP §7. *Later* means after Phase 7. *pre-release* is a gate before any public release.

### 1.6 Method

Requirements come from the roadmap sections named in each row. Status comes from the build notes and merge history on `develop`. Where a build note is silent about a point, the status is *Partial* or *Open*, not *Done*, and the backlog lists a verification spike for it. The code was not run for this review.

## 2. Overall description

### 2.1 Product perspective

Cinnabarr is the product of four upstream projects (ROADMAP §2): Sonarr supplies the base host, data layer, frontend and TV support; Radarr supplies movies; Prowlarr supplies the indexer engine; and qBittorrent, forked and headless, supplies the torrent engine. The engine runs as a separate process supervised by Cinnabarr, bound to loopback (ROADMAP §3, §5).

### 2.2 Product functions

- One library of series and movies, sharing profiles, tags and a single queue and history (§1, §4.1).
- Torrent search through the indexer engine, with proxies, stats and definitions from the owner's source (§4.3).
- A built-in engine whose torrents are reconciled, imported file by file, and seeded under per-file rules (§4.4, §4.8, §4.10).
- Version slots and races for one item (§4.9), reorganisation and migration (§4.12, Phase 6), and packaging with a contract migration (Phase 7).
- Future: a Jellyfin plugin and webOS integration (Addendum A), phone requests through a relay (Addendum B), and web sources through yt-dlp (Addendum C).

### 2.3 User classes

- **Owner.** The only user in the current plan. Uses the desktop browser to run the library. The owner sets up the metadata keys, the definitions source and the library folders.
- **Future users.** Not in the current plan. Addendum A (Jellyfin users on a TV) and Addendum B (single-user phone) are the first points where anyone else would touch the product.

### 2.4 Operating environment

- A .NET host and a C++ engine (Qt 6 and libtorrent 2.0.x, ROADMAP §5).
- SQLite or Postgres (§4.5).
- Windows, Linux, macOS and Docker, on x64 and arm64 (§5, NFR-08).
- The frontend is React on Vite, TypeScript and the Sonarr base stack (§6.4).

### 2.5 Design principles

The product principles are in ROADMAP §1 and bind every requirement: one library, one queue, one search, one settings tree, no outbound UIs, torrent-only, state always true, download straight to the final place, only video and subtitles reach disk, a flat consistent library, and optional seeding that never costs a file.

### 2.6 Assumptions and dependencies

- Series metadata stays on Sonarr's service, so no TVDB key is needed (BL-094 is not needed). Movies use the Radarr project's service. A TMDb key and attribution (BL-095) are only needed if movie metadata is later taken directly, and licence terms for the services in use are checked before public release (MET-06).
- The owner's indexer definitions fork stays reachable, with a bundled snapshot as the fallback (IDX-02, IDX-03).
- yt-dlp and ffmpeg are available to Addendum C. yt-dlp breaks often, so its version is pinned.
- GitHub Actions may not run on this repository, so local checks are the gate (AGENTS.md).
- Copilot no longer reviews pull requests. Each pull request is reviewed by its author, as AGENTS.md now requires (NFR-09).

## 3. Specific requirements

### 3.1 Functional requirements

Each table lists one group of functional requirements. A requirement shows its statement, priority, phase and status. The RTM adds the source, evidence and verification for each one.

#### 3.1.1 Library and media types

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| LIB-01 | One library for series and movies | The system shall show series and movies in one Library list with a type filter, sharing sort and bulk actions where built. | M | 4h | Partial |
| LIB-02 | Shared profiles, formats and tags | Quality profiles, custom formats, delay and release profiles, tags and notifications shall be shared by series and movies. | M | 4c | Done |
| LIB-03 | Media type on shared records | History, blocklist, queue items, pending releases and root folders shall carry a media type. | M | 4d | Done |
| LIB-04 | Library read model and projections | A LibraryItem projection shall give one list of common fields for both types, while Calendar and Wanted use their own episode and release projections. | M | 4h | Done |
| LIB-05 | Merged Wanted page | Missing and Cutoff Unmet shall show episodes and movies on one page with a type filter. | S | 4h | Partial |
| LIB-06 | Type-specific item pages | Series pages show seasons and episodes; movie pages show files, collection and credits. Both show seeding status per file, and version slots once Phase 5 lands. | S | 4g | Partial |
| LIB-07 | Import existing movie folders | The system shall list movie folders in a root folder that are not yet in the library, look each one up and import the chosen match in place. | S | 4g | Partial |
| LIB-08 | Movie import lists | Movie import lists shall add movies from external lists, as series import lists already do. | S | 4f | Open |
| LIB-09 | Discover in Add | The Add tab shall offer popular and upcoming titles, for series and movies. | S | 4g | Open |

#### 3.1.2 Download engine and Downloads page

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| ENG-01 | Supervised built-in engine | Cinnabarr shall start the engine, check its health, restart it with backoff and stop it on shutdown. Only the supervisor shall start the engine. | M | 2 | Done |
| ENG-02 | Loopback-only engine API | The engine API shall listen on 127.0.0.1 only, authenticated by an API key Cinnabarr generates. Cinnabarr writes all engine preferences. | M | 2 | Done |
| ENG-03 | One download client | The built-in engine shall be the only download client. External clients, Usenet clients and remote path mappings shall be removed. | M | 2 | Done |
| ENG-04 | Info-hash is the only key | Torrents shall be matched on the info-hash, including the v2 hash where present, never on name or path. | M | 2 | Done |
| ENG-05 | Cinnabarr owns intent, engine owns facts | Cinnabarr shall own intent (linked item, slot, seeding rule); the engine shall own facts (progress, state, files). | M | 2 | Done |
| ENG-06 | Reconcile without deleting | On start-up and every poll, unknown engine torrents shall be adopted as Unlinked and records whose torrent is gone shown as Missing from engine. Reconcile shall never delete. | M | 2 | Done |
| ENG-07 | Re-add or dismiss a missing grab | A grab missing from the engine shall be re-addable, or dismissed (recorded as ignored, nothing deleted). | S | 2 | Partial |
| ENG-08 | Sidecar recovery | Each grab shall write a sidecar in the engine profile holding media type, external IDs, season and episode, and release info, so a restored database can rebuild links. Slot label and seeding rule fields are added with §4.9 and §4.10. | M | 2 | Partial |
| ENG-09 | Every torrent is visible | Every engine torrent shall appear on the Downloads page, including seeding ones. Only automatic import and failure handling are limited to eligible downloads. | M | 2 | Done |
| ENG-10 | Downloads page is the only download UI | Downloads shall be the queue and the client: one row per torrent, status tabs, Add Torrent (magnet or .torrent), per-file details, live speeds, pause, resume, force start and re-check. | M | 2 | Done |
| ENG-11 | Per-row priority, speed and share limits | Downloads rows shall offer per-torrent priority, speed limit and share limit. | C | 2 | Open |
| ENG-12 | Direct-to-final download | Each torrent shall be added held, its whitelist applied, its kept files renamed to hidden pending names in their final folders, then started. No file reaches the library before it is checked. | M | 2 | Done |
| ENG-13 | Per-file import | Each file shall be validated and imported as soon as the engine reports it complete. The import is idempotent by info-hash and file index. | M | 2 | Done |
| ENG-14 | Engine-aware upgrades | An upgrade shall replace the old file only after the new one passes its checks. The old file goes to the recycle bin. | M | 2 | Done |
| ENG-15 | Movies download direct to the final place | Movie torrents shall use the direct-to-final flow with movie names, and import file by file. | M | 4 | Open |
| ENG-16 | Engine binaries built in CI | CI shall build cinnabarr-engine as a static binary for linux-x64, linux-arm64, win-x64 and macOS (x64 and arm64). | M | 7 | Open |
| ENG-17 | Engine binary name | The engine binary shall be named cinnabarr-engine, not cinnabarr-engine. | M | 1e | Open |
| ENG-18 | Engine pushes changes | The engine shall push changes to Cinnabarr instead of Cinnabarr polling it. | C | Later | Open |

#### 3.1.3 Files, checks and import

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| FIL-01 | File whitelist | Only video (mkv, mp4, m4v, avi, ts, m2ts, webm) and subtitle (srt, ass, ssa, vtt, sub, idx, sup) files shall be downloaded. Archives, and torrents with no video, shall be rejected and searched again. | M | 2 | Done |
| FIL-02 | Pre-download checks | Before any data downloads, the system shall check file names, extensions, sizes, the sample name and size, and that the parsed episode or movie matches the target. | M | 2 | Partial |
| FIL-03 | Post-completion video checks | After each video completes, the system shall read media info and run the runtime-based sample check. A rejected video shall be deleted, its release blocklisted and searched again. | M | 2 | Partial |
| FIL-04 | Subtitle validation | Subtitles shall be checked separately, attached to their video, renamed to match it. A bad subtitle shall be discarded without blocklisting the release. | M | 2 | Done |
| FIL-05 | Optional staging folder | An optional per-root staging folder shall hold torrent data until files pass checks, and stay hidden from scans. | S | 2 | Done |
| FIL-06 | Part files cleaned | Engine part files shall be removed with their torrent, and leftover partial files shall be cleaned up. | M | 2 | Done |
| FIL-07 | Movie file import | Movie files shall be imported (moving, upgrading, history), and finished movie downloads imported. | M | 4 | Done |
| FIL-08 | Movie extras | Movies shall keep subtitle extras. Other extras (.nfo, artwork) and subtitles already beside existing files on a scan remain to do. | S | 4 | Partial |
| FIL-09 | No video deleted without approval | Orphan cleanup shall delete only Cinnabarr's partial files, and only after an approved list. The reorganisation tool shall never delete a video without explicit approval. | M | 2, 6 | Partial |

#### 3.1.4 Versions and concurrent sources

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| VER-01 | Version slots | Each series and movie shall hold named version slots, each with its own quality profile, custom-format rules, monitored state, Wanted state and upgrades. Every item has a default slot. | M | 5 | Open |
| VER-02 | Download another version | Search shall offer 'Download another version' into an existing or new slot. | S | 5 | Open |
| VER-03 | Race mode | Up to N releases may race for one file in one slot. The first copy to complete, pass its checks and import wins. Losers are cancelled only after the winner is imported and are not blocklisted. Races are decided per file. | S | 5 | Open |
| VER-04 | Race safety | Race shall be off by default for private trackers, and per-indexer 'race allowed' shall gate it. | M | 5 | Open |
| VER-05 | Multi-version naming verified | Multi-version naming shall be checked against Emby, Jellyfin and Plex before the file-name format is locked. | M | 5 | Open |

#### 3.1.5 Seeding

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| SEED-01 | Optional seeding with defaults | Seeding shall be optional. The global default is ratio 1 or 14 days, whichever comes first, with a tracker minimum as a floor. | M | 2 | Done |
| SEED-02 | Scoped seeding goals | Goals may be set per file, season, series or movie. The most specific goal wins, and each goal shows where it came from. | S | 2 | Done |
| SEED-03 | Videos never deleted for seeding | When a goal is met, the torrent shall be removed from the engine and its files kept. | M | 2 | Done |
| SEED-04 | Per-file removal from a seeding torrent | Removing one file shall set its priority to 0, delete or replace it, then force a recheck. | M | 2 | Partial |
| SEED-05 | Overdue seeding flagged | A health check shall flag any active torrent that is past its seeding goal. | S | 2 | Open |

#### 3.1.6 Naming and layout

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| NAM-01 | Flat library layout | Series and movies shall use the §4.11 layout, with no torrent folders kept. | M | 2, 4 | Done |
| NAM-02 | Subtitle naming | Subtitles shall use the video's base name, its language, forced or SDH tag, and copy numbers. | S | 2 | Partial |
| NAM-03 | Multi-episode and specials naming | Multi-episode files shall use S01E01-E02, and specials shall go in Season 00. | S | base | Done |
| NAM-04 | Version label in file names | File names shall carry the version slot label, for example 2160p, HD or Remastered. | M | 5 | Open |
| NAM-05 | Movie naming settings and rename | Movie naming templates shall be editable, existing movie files shall be renamed when the format changes, and the movie folder shall be renamed too. | S | 4 | Partial |

#### 3.1.7 Migration, reorganising and maintenance

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| MIG-01 | Library reorganisation tool | The system shall scan, identify, plan, preview and execute a reorganisation of a messy library. Every run shall write a journal that can be undone, and torrent files shall be renamed through the engine. | M | 6 | Open |
| MIG-02 | Importers for existing installs | The system shall import Sonarr, Radarr and Prowlarr databases and qBittorrent BT_backup folders, so seeding continues without re-downloads. | M | 6 | Open |
| MIG-03 | Orphan cleanup | A daily task shall list leftover partial files as a dry run. It shall delete only after approval, respect the grace period, and check engine ownership. | M | 2 | Done |
| MIG-04 | External contracts migration | One migration shall rename the external contracts (database file, AppData folder, service and binary names, ports, environment variables, script variables) with backups, identity records and a rollback path. | M | 7 | Open |
| MIG-05 | Ownership check before Phase 7 | Before Phase 7, startup writes and legacy folder migrations shall require ownership confirmation for any folder not newly created. An unconfirmed folder shall get a separate data path. | M | 1e–7 | Open |
| MIG-06 | Backups include the engine profile | Cinnabarr backups shall include the engine profile (resume data and settings). | M | 2 | Partial |

#### 3.1.8 Metadata

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| MET-01 | Interim movie metadata | Until Phase 4i, movie metadata shall come from the Radarr project's metadata service. | M | 4a | Done |
| MET-02 | Series metadata from Sonarr's service | Series data comes from Sonarr's Skyhook service (which reads TVDB). The owner has no TVDB key and chose to keep using it (changed from "TVDB directly"). | M | 4i | Decided: keep Skyhook |
| MET-03 | Movie metadata from TMDb | Movie data shall come from TMDb directly with the owner's key, replacing the interim service. | M | 4i | Open |
| MET-04 | IMDb identifiers | IMDb IDs and ratings shall come via TMDb external IDs. IMDb datasets are optional. | S | 4i | Open |
| MET-05 | Scene mappings | Scene name mappings come from Sonarr's scene mapping service, as in Sonarr (changed from TheXEM plus an own list, which needed the TVDB work). | M | 4i | Decided: keep Sonarr's service |
| MET-06 | Metadata licence terms | Metadata use shall comply with the TVDB, TMDb and IMDb terms. Attribution and project keys shall be in place before any public release. | M | 4i | Open |

#### 3.1.9 Search and indexers

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| IDX-01 | Indexer engine core | The indexer engine core (definitions, status, limits, HTTP, capabilities, search criteria, releases and Torznab) shall be ported from Prowlarr under NzbDrone.Core.IndexerEngine. | M | 3 | Done |
| IDX-02 | Definitions from the owner's source | Indexer definitions shall come from the owner's fork (flintstrike-nz/Indexers, highest v-folder up to v12). Prowlarr's servers shall never be contacted. | M | 3 | Done |
| IDX-03 | Bundled definitions snapshot | Each release shall bundle a definitions snapshot, used when the fork cannot be reached. | M | 7 | Open |
| IDX-04 | Saved indexers run on the engine's HTTP stack | Saved torrent indexers shall send requests through the engine's HTTP stack, with proxies, rate limits and Cloudflare handling. Unsupported ones shall be kept, switched off and flagged. | M | 3 | Done |
| IDX-05 | Cardigann adapter | Cardigann indexers shall map search criteria (IDs first, scene titles as fallback, daily, anime, specials) and releases, and download torrent files through the engine. | M | 3 | Done |
| IDX-06 | Add indexers from definitions | Users shall add indexers from the definition list, with their login fields, filtered to torrent indexers. | M | 3 | Done |
| IDX-07 | Indexer proxies | FlareSolverr, HTTP and SOCKS proxies shall be configurable by tag, with an API and a settings section. | S | 3 | Done |
| IDX-08 | Indexer stats and query history | Every RSS fetch and search shall be recorded per indexer, and shown under Activity. | S | 3 | Done |
| IDX-09 | Indexer test and flags | Settings → Indexers shall offer test and flags for engine indexers. | S | 3 | Partial |
| IDX-10 | Native Prowlarr torrent indexers | Prowlarr's native torrent indexers shall run natively, retiring Sonarr's native indexer classes as each gets an engine equivalent. | C | 3 | Hold |
| IDX-11 | Search page | One free-text search across interactive indexers shall offer grab (with an optional link to a library item). Results shall pass through both decision makers. | M | 4 | Partial |
| IDX-12 | Usenet removed | Usenet indexers and clients shall be removed, keeping only the Newznab shared code that Torznab uses. | M | 2 | Done |
| IDX-13 | Definition refresh schedule | Definitions shall refresh at start-up and then daily. | S | 3 | Done |

#### 3.1.10 API

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| API-01 | One API and key | Cinnabarr shall expose one API for series, movies, library, queue, indexers, search and engine, protected by one API key. | M | 2–4 | Partial |
| API-02 | Remove the v3 API | The Sonarr v3 API shall be removed once the frontend no longer uses it. | S | 7 | Open |
| API-03 | Optional v3 compatibility shims | Optional Sonarr and Radarr v3 shims shall let request tools (Overseerr, Jellyseerr) talk to Cinnabarr. | C | 7 | Open |
| API-04 | Release push routes movies | The release push endpoint shall send movie releases to the movie decision maker as well as the series one. | S | 4 | Done |

#### 3.1.11 User interface

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| UI-01 | Navigation | The sidebar shall offer Library, Add, Calendar, Wanted, Downloads, Search, Activity, Settings and System in place of Sonarr's sidebar. | M | 1 | Done |
| UI-02 | Add tab | One search box shall find TV and movie metadata together, with a type badge on each result, and tabs for Search, Discover and Import Existing Folders. | M | 4g | Partial |
| UI-03 | Merged Calendar | The Calendar and its iCal feed shall include episodes and movie releases, with an agenda view. | M | 4h | Partial |
| UI-04 | Merged Activity | History and Blocklist shall show both media types, paged and filtered by type. | M | 4g | Open |
| UI-05 | Library bulk actions | The Library grid shall offer bulk search, monitor, quality profile and delete, plus bulk tags. | S | 4h | Open |
| UI-06 | Movie screens rebuilt on Cinnabarr patterns | Movie screens shall be rebuilt on Cinnabarr's frontend patterns, not copied from Radarr. | M | 4g | Done |
| UI-07 | Frontend stack | The frontend shall use Zustand, TanStack Query, React Router 7 and CSS modules, from the Sonarr base. | M | base | Done |
| UI-08 | Settings tree | Settings shall follow the §6.3 tree, each setting in one place. Items removed by §6.3 shall be gone from the UI. | S | 2–6 | Partial |
| UI-09 | Browser check for each UI step | Each UI step shall be checked in a browser before merge, not only with lint and build. | S | process | Partial |

#### 3.1.12 Settings and system

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| SET-01 | Analytics off | Analytics shall be off, and Sonarr's analytics and cloud calls removed. | M | 1 | Done |
| SET-02 | Default version slots and race defaults | Media Management and Downloads settings shall hold default version slots and race defaults. | S | 5 | Open |
| SET-03 | Engine legal notice at first run | The engine's legal notice shall be shown once, at first run. | M | 2 | Open |
| SET-04 | Downloads settings | Downloads settings shall include listening port, UPnP and NAT-PMP, interface binding, speed limits and schedule, queue limits, privacy options and disk pre-allocation. | S | 2 | Partial |

#### 3.1.13 Naming and branding

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| BRD-01 | Phase 1 rename to Cinnabarr | Product name, logos and user-visible strings shall read Cinnabarr. | M | 1 | Partial |
| BRD-02 | Phase 1e rename to Cinnabarr | The product, UI, logos, theme and metadata shall read Cinnabarr, using the owner's theme and icons. | M | 1e | Open |
| BRD-03 | Name clearance | The name shall be checked against trademark registers, app stores, GitHub and domain availability before any public release. | M | pre-release | Open |
| BRD-04 | Upstream namespaces kept | NzbDrone.* namespaces and project names shall stay until the roadmap says otherwise, so upstream fixes can be cherry-picked. | M | 1–4 | Ongoing |

#### 3.1.14 Packaging, platforms and process

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| PKG-01 | Single Docker image | One Docker image shall run both the host and the engine. | M | 7 | Open |
| PKG-02 | Desktop and Linux packages | Windows installer and service, a macOS app and Linux packages shall be produced. | M | 7 | Open |
| PKG-03 | Own update channel | Cinnabarr shall update from its own channel, not Sonarr's. | M | 7 | Open |
| PKG-04 | Platform targets | Builds shall cover linux-x64, linux-arm64, win-x64 and macOS x64 and arm64. | M | 7 | Open |
| PKG-05 | User documentation | User documentation shall cover install, migration and settings. | S | 7 | Open |
| PKG-06 | Checks on every change | Build, lint, typecheck and tests shall run on every change. Local checks are the gate while GitHub Actions may not run. | M | 1 | Partial |


### 3.2 External interfaces

| ID | Interface | Description | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| IF-01 | Engine private API | The host shall call the engine's private API (a qBittorrent WebAPI subset) on 127.0.0.1 with the API key. Calls used: app/version, app/setPreferences, transfer/info, transfer/speedLimitsMode, torrents/stop/start/recheck, file priority and rename, share limits, tags. | M | 2 | Done |
| IF-02 | Metadata providers | TVDB v4 (project key), TMDb (key), IMDb datasets (optional) and TheXEM for scene mappings. | M | 4i | Open |
| IF-03 | Interim movie metadata service | Radarr project's metadata service, used for movies until Phase 4i. | M | 4a | Partial |
| IF-04 | Sonarr metadata service (Skyhook) | Called for series data, scene mappings, daily-series data and the TMDb import-list proxy; kept by decision. It is a third party's service, so its availability and terms are a risk (MET-06). | M | base | Done |
| IF-05 | Indexer definitions fork | flintstrike-nz/Indexers, zip download of definitions/v{n} files. | M | 3 | Done |

### 3.3 Non-functional requirements

| ID | Requirement | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| NFR-01 | Truthful state | The system shall never hide, lose or silently stop seeding a torrent. The Downloads page shows everything the engine holds. | M | 2 | Done |
| NFR-02 | File safety | No video shall be deleted without explicit approval, except a rejected download under FIL-03. | M | all | Done |
| NFR-03 | Restart resilience | After a restart the system shall rebuild links from sidecars, re-check engine state and never import a file twice. | M | 2 | Partial |
| NFR-04 | Responsive Downloads page | The Downloads page shall refresh within the intervals set in the roadmap: engine status every 3 s, file details every 10 s, sync every 1–2 s while open. | S | 2 | Done |
| NFR-05 | No telemetry | The product shall send no analytics or usage data. | M | 1 | Done |
| NFR-06 | Licence | The combined distribution shall be GPLv3. The engine shall stay a separate executable. Upstream notices shall be kept. | M | all | Ongoing |
| NFR-07 | Clean test baseline | The core test suite shall pass with no failures. | M | all | Open |
| NFR-08 | Platform support | Windows, Linux, macOS and Docker shall be supported, on x64 and arm64. | M | 7 | Open |
| NFR-09 | Author review without Copilot | Each PR shall be reviewed by its author for correctness, safety of users' files and the repo's checks. Findings shall be addressed or answered on the thread. | M | process | Done |
| NFR-10 | Translations | Translations shall move to a Cinnabarr project once Phase 4 is complete. | C | 4+ | Open |
| NFR-11 | No secrets in the product | No API keys, tokens or indexer credentials shall be bundled in releases or stored off the owner's machines. | M | all | Ongoing |
| NFR-12 | Upstream cherry-pick | Upstream fixes shall stay cherry-pickable until the namespace rename (open decision 1). | S | 1–4 | Ongoing |

### 3.4 Constraints

| ID | Constraint | Statement | Priority | Phase | Status |
| --- | --- | --- | --- | --- | --- |
| CON-01 | Two processes | The host is a .NET process and the engine is a separate C++ process. | M | all | Done |
| CON-02 | Database choice | Both SQLite and Postgres shall remain supported. | M | all | Ongoing |
| CON-03 | Migration chain | The roadmap says to start a fresh migration chain at migration 1 (§4.5). Built work adds migration 242 to Sonarr's chain (3b-1). See decision BL-080. | M | 4 | Open |
| CON-04 | No Usenet | Usenet indexers and download clients are out of scope. | M | all | Done |
| CON-05 | Torrent-only engine scope | Downloads go through the built-in engine only. Other download clients are out of scope (open decision 3). | M | all | Done |

### 3.5 Future addenda (not in the current plan)

These are recorded for traceability. They are not scheduled and must not delay or reshape Phases 1 to 7 (Addenda A to C).

| ID | Item | Statement | Priority | Status |
| --- | --- | --- | --- | --- |
| A-01 | Jellyfin server plugin | The Jellyfin plugin shall store the Cinnabarr URL and API key, decide which Jellyfin users may add items, expose authenticated endpoints, and record who requested what. | S | Future |
| A-02 | Discover and Add web layer | A web layer on jellyfin-web shall add Discover and Add to Cinnabarr, with quality and version options. | S | Future |
| A-03 | Add and status contract | Cinnabarr shall expose lookup, add (with defaults and options), discover lists and an 'already in Cinnabarr' status, shared with Addendum B. | S | Future |
| A-04 | webOS remote-only use | The stock jellyfin-webos app shall work with remote-only navigation. | S | Future |
| B-01 | Vercel relay queue | The relay shall store requests and a status snapshot, accept only the closed command set (add_series, add_movie), and never call Cinnabarr. | S | Future |
| B-02 | Home relay client | Cinnabarr shall poll the relay outward, validate each request against a schema, call its own API and report results. It is off by default. | S | Future |
| B-03 | Status snapshot and offline view | The home server shall send a compact library and queue snapshot. The phone shall show when the home server last checked in. | S | Future |
| B-04 | Security of the relay | Separate revocable credentials for phone and home server, hashed storage, signed requests, rate limits and expiry. | M | Future |
| B-05 | Android APK | A trimmed APK (Discover, Requests, Library status) shall call the relay, not Cinnabarr directly. | S | Future |
| C-01 | NHK World listing | Cinnabarr shall list an NHK World show's episodes, first through yt-dlp, and by a definition only if yt-dlp cannot. | S | Future |
| C-02 | yt-dlp download engine | A second download engine shall wrap yt-dlp as a subprocess with JSON output, progress, error capture and a pinned version. | S | Future |
| C-03 | Verify downloads | Downloads shall be checked against the listed duration before import, and partial files removed. | S | Future |
| C-04 | Private definitions repository | Site definitions, their schema and fixtures shall live in a separate private repository, fetched at a pinned tag or commit. | S | Future |
| C-05 | YouTube source | Each YouTube channel or playlist shall be a series, polled through its Atom feed, opt-in per source. | S | Future |
| C-06 | Live and short filters | YouTube live streams, Shorts and premieres shall be filtered by default. | C | Future |
| C-07 | Zero-results alert | Zero episodes found shall raise an alert and never mark episodes as removed. | M | Future |
| C-08 | Date-based matching | Date-based sources shall use the daily series type, with seasons by year, and match by air date first. | S | Future |
| C-09 | Opt-in and cookies | Each source shall be an explicit opt-in, and any cookies file shall be stored securely. | M | Future |

## 4. Findings from this review

The review found ten places where the roadmap, the built code and the notes disagree, or where a claim cannot be confirmed. They are listed below with the backlog item that resolves each one.

| ID | Topic | Finding | Backlog |
| --- | --- | --- | --- |
| D-01 | API path | §4.6 names /api/v1. Built features use /api/v5 (for example /api/v5/engine and /api/v5/moviefile). | BL-081 |
| D-02 | Migration chain | §4.5 says to start a fresh chain at migration 1. Built work adds migration 242 to Sonarr's chain (3b-1). | BL-080 |
| D-03 | Engine binary name | §5 and §4.7 say cinnabarr-engine. Commit #9 is titled 'supervise cinnabarr-engine', and the code name is not confirmed. | BL-005 |
| D-04 | Interim movie metadata | Movies use the Radarr project's metadata service (api.radarr.video) until 4i. §8 says not to ship publicly on a third party's service. | BL-015 |
| D-05 | Series metadata still Sonarr's | Series data still comes from Sonarr's Skyhook service (§8 TV metadata). | BL-014 |
| D-06 | Verification gap | Several steps were checked with lint and build only, not in a browser (notes 4g-5, 4h-4). | BL-084 |
| D-07 | Baseline failures | Core tests show 18 known failures in later build notes. | BL-082 |
| D-08 | Phase 1 brand text | Header and sidebar still render Sonarr, though #4 (1c) reported the rebrand done. | BL-001 |
| D-09 | Downloads ↔ Activity | Activity History and Blocklist remain series-only per the latest notes. | BL-019 |
| D-10 | Phase 4 Discover | Discover (§6.1, Radarr Discover) has no build note. | BL-018 |

The owner decisions below block the most work, and should be settled first:

1. **D-01 API path.** Whether `/api/v5` or `/api/v1` is the public path. Affects every client, and Addenda A and B.
2. **D-02 Migration chain.** Whether to follow §4.5 and start a fresh chain, or keep Sonarr's chain that the built work already extends. This is the biggest structural decision left.
3. **D-04 and D-05 Metadata.** Obtaining the TVDB and TMDb keys and checking licence terms. Public release is blocked until this is done (MET-06).
4. **Ownership marker timing (BL-096).** Whether the marker ships before Phase 7. It protects independent Sonarr installs, and the roadmap says the protection is needed before Phase 7.

## 5. Glossary

| Term | Meaning |
| --- | --- |
| Cinnabarr | The product (working name Embarr until the rename). |
| Engine | The headless qBittorrent-based torrent process, `cinnabarr-engine`. |
| Info-hash | A torrent's identity. The only key for matching engine torrents (ENG-04). |
| Sidecar | A small file per torrent in the engine profile that records what the grab was for (ENG-08). |
| Unlinked | An engine torrent Cinnabarr has no record of. It is shown on Downloads, not hidden. |
| Missing from engine | A record whose torrent the engine no longer has. Re-add or dismiss (ENG-07). |
| Direct-to-final | Downloading straight into the library folder under a hidden pending name (ENG-12). |
| Whitelist | The only file types downloaded: video and subtitle extensions (FIL-01). |
| Version slot | A named version of one item, for example 4K or HD (VER-01). |
| Race | Several releases competing for one file, with the first valid copy kept (VER-03). |
| Ownership marker | A marker in a data folder Cinnabarr manages, proving the folder is its own (MIG-05). |
| External contract | A name other software or users rely on: database file, AppData folder, service, environment variables, script variables (MIG-04). |
| Definitions | Cardigann indexer definitions, read from the owner's fork (IDX-02). |
| Torznab | A torrent indexer standard, kept from Newznab's shared code (IDX-12). |
| Skyhook | Sonarr's cloud metadata service, still used for series until Phase 4i (MET-02). |
| TheXEM | A community source of scene name mappings (MET-05). |
| yt-dlp | A command-line downloader used by Addendum C as a subprocess (C-02). |
| Relay | The Vercel mailbox in Addendum B that holds requests, and never calls the home server. |

## 6. Appendix: traceability

Each requirement's source, evidence, verification and backlog item are in the [RTM](02-RTM.md). Open work is in the [product backlog](04-product-backlog.md).
