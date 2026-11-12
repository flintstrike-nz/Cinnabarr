# Cinnabarr: Product Requirements Document (PRD)

| Item | Value |
| --- | --- |
| Document | PRD, draft v0.1 |
| Date | 2026-11-06 |
| Baseline | `origin/develop` at `8507a4c` |
| Status | Draft for owner review. Not yet agreed. |
| Companions | [SRS / FRD](01-SRS-FRD.md), [RTM](02-RTM.md), [Product backlog](04-product-backlog.md) |

## 1. Summary

Cinnabarr is one self-hosted app that runs a media library of series and movies, finds and downloads what the owner wants, keeps seeding under the owner's rules, and imports every file into a clean library. It replaces four separate apps (Sonarr, Radarr, Prowlarr and qBittorrent) with one that shares a single state and a single queue. It is for the owner's personal use.

## 2. Problem

The owner runs four apps that each keep their own state. They are joined by categories, path mappings and polling, so the state drifts apart (ROADMAP §4.4). In practice this causes these problems:

- Torrents that are still seeding disappear from the queue, and nothing shows how close they are to their goals.
- Downloads land as torrent folders, partial files and misnamed files, so the library needs manual clean-up.
- Series and movies need separate queues, histories and settings.
- Metadata and indexer definitions depend on services run by other projects (Sonarr's Skyhook, Radarr's metadata proxy, Prowlarr's definition servers).
- The owner cannot add a title from the TV or phone without opening a web app.
- Some shows, such as NHK World programmes, leave the catalogue without warning, and the owner has already lost some (NHKollector's README, Addendum C.2).

## 3. Vision

One app with one mental model (ROADMAP §1): one library, one queue, one search, one settings tree, one history. Nothing is hidden, nothing is lost, and every file reaches the library only after it has been checked.

## 4. Users and jobs

The only user in the current plan is the owner. Future users are described in Addenda A and B.

| Job | Today (baseline) | Status |
| --- | --- | --- |
| J1: Add a series or movie and have it downloaded | Movies and series add from one Library; movies downloaded and imported end to end | Largely done |
| J2: Search torrent indexers and grab a release | Indexers from 580 definitions, search page, proxies | Largely done |
| J3: See every download and its seeding goal in one place | Downloads page with per-file details and goals | Done |
| J4: Hold 4K and HD of the same title, and race sources | Version slots and races are not built | Not started (Phase 5) |
| J5: Move an existing Sonarr, Radarr, Prowlarr and qBittorrent setup across | Importers and reorganise tool are not built | Not started (Phase 6) |
| J6: Request a title from a phone (Addendum B) | Not started | Future |
| J7: Add a title from a TV (Addendum A, webOS) | Not started | Future |
| J8: Follow a web show, such as NHK World, and fetch new episodes (Addendum C) | NHKollector did this in 2025; not yet built in Cinnabarr | Future |

## 5. Goals and measures

Each goal maps to requirements in the [RTM](02-RTM.md) and to backlog items in the [backlog](04-product-backlog.md).

| Goal | Measure of success | Where it is tracked |
| --- | --- | --- |
| G1: One app, one queue, one search, one settings tree | No routine task needs another app's interface. The Downloads page is the only download UI. | ENG-10, LIB-01, UI-01 |
| G2: Never lose, hide or stop seeding a torrent. Never delete a video without approval. | Zero torrents hidden or lost after a restart in tests. Orphan clean-up deletes only after approval. Test baseline has no failures. | ENG-09, NFR-01, NFR-02, NFR-07 |
| G3: Automatic, reliable, safe imports | No file reaches its final name before its checks pass. A repeated completion never imports twice. | ENG-12, ENG-13, FIL-01 to FIL-04 |
| G4: Several versions of one item, and races for one file | 4K and HD held side by side. A race keeps exactly one copy. | VER-01 to VER-05 (Phase 5 exit) |
| G5: Follow any web source, not one site | NHK World episodes listed without a hard-coded site. YouTube sources opt-in. | C-01 to C-09 (Future) |
| G6: Add and browse from a TV or phone | A phone request reaches the library within one poll interval. A webOS TV can add a title. | A-01 to A-04, B-01 to B-05 (Future) |
| G7: Migrate existing installs and rename to Cinnabarr safely | An existing setup moves over with seeding intact, and the migration can be rolled back. | MIG-01 to MIG-06, BRD-01 to BRD-04, PKG-01 to PKG-05 |
| G8: Own the metadata and indexer definitions | No calls to Sonarr, Radarr or Prowlarr services once Phase 4 is done. Definitions come from the owner's fork. | MET-01 to MET-06, IDX-02, API-02 |

## 6. Scope and release plan

The phases follow ROADMAP §7. The status column is the baseline from the [RTM](02-RTM.md).

| Phase | Scope | Status at baseline | Exit criteria (roadmap) |
| --- | --- | --- | --- |
| 1: Rebrand and shell | Names, navigation, analytics off, CI | 1a to 1d done. Header and sidebar still say Sonarr. | Working TV-only shell; nothing in the UI says Sonarr |
| 1e: Rebrand to Cinnabarr | User-visible rename with the owner's theme and icons | Waiting on theme and icons | Nothing in the UI says Cinnabarr or Sonarr, except legacy handling |
| 2: Built-in engine | Engine, reconcile, direct-to-final, seeding, orphan clean-up, Downloads page | Done | Grab, download into the library and seed, with no external client |
| 3: Indexer engine | Prowlarr's engine, definitions, proxies, stats, add from definitions | Mostly done. 3c on hold. | Any Prowlarr torrent indexer usable for RSS, automatic and manual search |
| 4: Movies and merged library | Movies, shared profiles, one Library, Calendar and Wanted | Largely done. 4e-4, 4f and 4i open. | Movies and series side by side, one queue, one history, no Sonarr or Radarr services |
| 5: Versions and races | Version slots, per-slot upgrades, race mode, multi-version naming | Not started | 4K and HD of one item; race keeps one copy |
| 6: Reorganise and migrate | Reorganise tool and importers | Not started | An existing install moves to Cinnabarr in one undoable run |
| 7: Packaging and ecosystem | Docker, installers, update channel, contracts migration, shims | Not started | Packages for every platform, migration tested, rollback available |
| Addenda A to C | Jellyfin, relay and web sources | Future | Not in the current plan |

The current plan is Phases 1 to 7. Addenda A to C must not delay or reshape it (ROADMAP Addenda).

## 7. Non-goals

- **Multi-user accounts.** Single user only in the current plan. Addendum B says so explicitly.
- **Usenet and external download clients.** Removed by decision (CON-04, CON-05). A possible reversal is open decision 3.
- **Playback.** Cinnabarr manages the library. Emby, Jellyfin and Plex play it (§4.11, Addenda A and C).
- **Circumventing DRM or access controls** (Addendum C.9, §8).
- **Public hosting or a public service.** Public release is gated (section 8).

## 8. Release gates before any public release

Public distribution is blocked until each of these is met:

1. **Name clearance** for Cinnabarr across trademark registers, app stores, GitHub and domains (BRD-03; ROADMAP §9 decision 8).
2. **Metadata licences**: TVDB project key and licence, TMDb key and attribution, IMDb dataset terms (MET-06; §8).
3. **Series and movie metadata** come from TVDB and TMDb directly, not Sonarr's or Radarr's services (MET-02, MET-03; §8).
4. **Packaging and the contracts migration** (Phase 7), so installs can be upgraded and rolled back (MIG-04, PKG-01 to PKG-03).
5. **GPLv3 compliance**: licence and upstream notices, and the engine kept as a separate executable (NFR-06).
6. **Engine legal notice** shown once at first run (SET-03).
7. **User documentation** (PKG-05).
8. **No credentials bundled** in any release (NFR-11).

Private, personal use is not gated by these. The owner decides when a gate is met.

## 9. Dependencies and risks

| Risk or dependency | Effect | Mitigation | Backlog |
| --- | --- | --- | --- |
| Metadata keys and licences (TVDB, TMDb, IMDb) | Blocks series and movie metadata, and public release | Obtain keys; check terms before release | BL-094, BL-095, MET-06 |
| Interim use of a third-party metadata service | Movies depend on Radarr's service until Phase 4i | Replace in 4i | BL-015 |
| Series still on Sonarr's Skyhook service | Same dependency, for series | Replace in 4i | BL-014 |
| Migration chain conflict (§4.5 vs built code) | Structural decision affects Phases 4 to 7 | Owner decision first | BL-080 |
| API path conflict (/api/v1 in roadmap, /api/v5 in code) | Affects every client and addendum | Owner decision first | BL-081 |
| Upstream drift (Sonarr, Radarr, Prowlarr, qBittorrent) | Fixes need cherry-picking | Keep namespaces; cherry-pick on a schedule | NFR-12 |
| Native builds (Qt 6, libtorrent, OpenSSL) on five platforms | Hardest part of CI | Build engine in its own pipeline | ENG-16, PKG-04 |
| Verification gap: several steps checked only by lint and build | Behaviour in a browser unproven | Browser check for each UI step | UI-09, BL-084 |
| Baseline test failures (18) | Regressions hard to see | Clear the baseline | NFR-07, BL-082 |
| CI may not run on this repo | Local checks are the only gate | Confirm the gate | PKG-06, BL-083 |
| Copilot reviews no longer available | Each PR is reviewed by its author | Author review checklist (AGENTS.md) | NFR-09 |
| Site and definition changes (indexers, NHK World) | Downloads break without warning | Fixtures, zero-results alert, pinned versions | C-07, BL-124 |
| yt-dlp breakage (Addendum C) | Downloads break often, especially for YouTube | Pin and update deliberately | C-02 |
| Legal exposure for web sources | Takedown or terms issues | Private definitions; per-source opt-in; no DRM | C-04, C-09 |
| Scope: four mature apps merged | Large programme of work | Ship each phase as a working build | Phases 4 to 7 |

## 10. Decisions needed from the owner

Settle these first, because they block the most backlog items.

1. **Migration chain** (BL-080). Follow §4.5 and start a fresh chain, or keep Sonarr's chain that built work already extends?
2. **API path** (BL-081). `/api/v1` as the roadmap says, or `/api/v5` as the code uses?
3. **Metadata keys** (BL-094, BL-095). Obtain the TVDB and TMDb keys?
4. **Ownership marker timing** (BL-096). Ship it before Phase 7, as the roadmap's ownership rule implies?
5. **Repository and folder rename** (BL-004). Rename `EmbarrPre1` and `docs/cinnabarr/` during Phase 1e, or later?
6. **Race winner rule** (BL-093, §9 decision 5) and **maximum version slots** (BL-058, §9 decision 6).
7. **Addendum C first step** (BL-120). The owner re-runs NHKollector against the live NHK site to test whether yt-dlp can list the shows.
8. **Private definitions repository** (BL-121). Create `cinnabarr-definitions` as a private repo.
9. **Phase 7 follow-up items** (BL-087). Add the two low-severity items from PR #10 (legacy backup identity and rollback limits) to the roadmap now?
10. **Backlog tool** (BL-088). Use GitHub Projects, as recommended in the backlog.

## 11. Document set

- [01-SRS-FRD.md](01-SRS-FRD.md): what the product must do, and the status of each requirement.
- [02-RTM.md](02-RTM.md) and [02-RTM.csv](02-RTM.csv): traceability from requirement to source, evidence, verification and backlog item.
- [03-PRD.md](03-PRD.md) (this document): why, for whom, in what order, and what must be true before release.
- [04-product-backlog.md](04-product-backlog.md) and [04-product-backlog.csv](04-product-backlog.csv): the work still to do, ready to import into GitHub Projects.
