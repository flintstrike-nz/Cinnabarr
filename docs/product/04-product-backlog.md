# Cinnabarr: Product backlog

Baseline: `origin/develop` at `8507a4c`. Each item traces to requirements in the [RTM](02-RTM.md). Items are grouped by epic and sequenced as Now, Next, Later, Hold and Future. Items marked *Blocked* or *Decision* need the owner before they can start.

## Tool recommendation

Use **GitHub Projects (v2)** on `flintstrike-nz/EmbarrPre1`, with [04-product-backlog.csv](04-product-backlog.csv) as the import source.

- The code, pull requests and roadmap already live on GitHub, so each backlog item can link to its PR. Projects v2 is free for private repositories.
- Miro is good for a visual story map, but a poor system of record. Its connector is also disconnected in this session, so it cannot be built or updated from here. A Miro board can be fed from the same CSV later if a visual view is wanted.
- Nothing has been created in GitHub yet. Creating issues or a project needs the owner's go-ahead, so that the backlog stays under the owner's control.

### How to import

1. Create a GitHub Project (table view) on the repo.
2. Add the fields: Epic (single select), Type, Priority, Size, Sequence, Status, Phase, Requirements (text), Depends on (text).
3. Import `04-product-backlog.csv` (GitHub's CSV import or a script), or paste the rows.
4. Optionally create one issue per item, with the requirement IDs in the body, so PRs can close them.

## Summary

| Sequence | Items |
| --- | --- |
| Now | 18 |
| Next | 35 |
| Later | 24 |
| Future | 14 |
| Total | 91 |

| Epic | Items | Must | Should | Could |
| --- | --- | --- | --- | --- |
| Rebrand | 5 | 4 | 1 | 0 |
| Phase 4 close-out | 16 | 7 | 8 | 1 |
| Indexers | 2 | 1 | 0 | 1 |
| Engine | 10 | 5 | 3 | 2 |
| Versions | 10 | 4 | 6 | 0 |
| Migration | 6 | 5 | 1 | 0 |
| Release | 13 | 8 | 3 | 2 |
| Decisions | 6 | 2 | 4 | 0 |
| Quality | 3 | 2 | 1 | 0 |
| Process | 1 | 1 | 0 | 0 |
| Owner inputs | 3 | 3 | 0 | 0 |
| Addendum A | 4 | 1 | 3 | 0 |
| Addendum B | 4 | 1 | 3 | 0 |
| Addendum C | 8 | 4 | 4 | 0 |

## Items by epic

### Rebrand

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-001 | Fix the header and sidebar still reading Sonarr | Task | BRD-01 | 1 | M | S | none | Now | Done |
| BL-002 | Phase 1e rename to Cinnabarr (UI, strings, logos, metadata, docs) | Story | BRD-02 | 1e | M | M | BL-004, BL-005 | Next | Blocked: owner theme and icons |
| BL-003 | Name clearance search before any public release | Task | BRD-03 | pre-release | M | S | none | Later | Ready |
| BL-004 | Decide whether to rename the repo and docs/embarr folder | Decision | BRD-02 | 1e | S | XS | none | Next | Decision |
| BL-005 | Rename the engine binary to cinnabarr-engine and check the code | Spike | ENG-17 | 1e | M | S | none | Next | Ready |

### Phase 4 close-out

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-010 | Movies straight into the final place via the engine (4e-4) | Story | ENG-15, FIL-07 | 4 | M | L | none | Now | Done for one movie video with its subtitles (checked against the real engine); upgrade replacement only unit-tested; a movie link is restored from its sidecar after a database restore (checked) |
| BL-011 | Movie folder rename and token picker for movie naming (4e-5) | Story | NAM-05 | 4 | S | M | none | Next | Partial: moving a movie folder and renaming it to the folder format from Organize done; token picker open |
| BL-012 | Movie extras: .nfo, artwork, and subtitles beside existing files on scan | Story | FIL-08 | 4 | S | M | none | Next | Done (checked in the real app; folder-wide artwork such as poster.jpg is not linked) |
| BL-013 | Movie import lists (4f) | Story | LIB-08 | 4 | M | M | none | Next | Partial: Done for two sources (custom JSON list, TMDb trending/popular); other sources, exclusions and the UI language field open |
| BL-014 | Series metadata from TVDB directly, replacing Skyhook (4i) | Story | MET-02, MET-06, IF-02, IF-04 | 4 | M | L | BL-094 | Now | Blocked: TVDB key and licence |
| BL-015 | Movie metadata from TMDb directly, replacing the interim service (4i) | Story | MET-03, IF-03 | 4 | M | M | BL-095 | Next | Blocked: TMDb key |
| BL-016 | TheXEM scene mappings plus Cinnabarr's own list (4i) | Story | MET-05, MET-04 | 4 | M | M | BL-014 | Next | Ready |
| BL-017 | Collections and credits on movie pages | Story | MET-06, LIB-06 | 4 | S | M | none | Next | Partial: credits and collection shown; collection pages and person pages open |
| BL-018 | Discover tab and one Add search for TV and movies | Story | LIB-09, UI-02 | 4 | M | L | none | Next | Partial: movie Discover done; one Add search for series and movies, and series Discover, open |
| BL-019 | Merged Activity: History and Blocklist for both media types | Story | UI-04 | 4 | M | M | none | Next | Done (paging and filters for the movie sections still open) |
| BL-020 | One Wanted page for episodes and movies | Story | LIB-05 | 4 | S | M | none | Next | Done |
| BL-021 | Calendar grid filters, iCal feed and agenda include movies | Story | UI-03 | 4 | S | M | none | Next | Done (status colours and detail popup for movie blocks still open) |
| BL-022 | Library bulk actions on the merged grid, and bulk tags | Story | UI-05, LIB-01 | 4 | S | M | none | Next | Partial: select mode with bulk search, monitor, quality profile and delete done; bulk tags open |
| BL-023 | Movie folder import: import all matched, re-match, nested folder formats | Story | LIB-07 | 4 | S | M | none | Next | Partial: re-match and Import All done; nested folder formats open |
| BL-024 | Movie media-info detail (audio and subtitle streams) | Task | LIB-06 | 4 | C | S | none | Later | Done (not checked with a real file) |
| BL-025 | Search page gaps: grab titles not in the library, category and indexer filters, sortable columns | Story | IDX-11 | 4 | S | M | none | Next | Partial: sorting, indexer filter and add links done; category filters and grabbing without adding open |

### Indexers

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-030 | Settings → Indexers: test and flags for engine indexers (3f) | Story | IDX-09 | 3 | M | S | none | Next | Ready |
| BL-031 | Native Prowlarr torrent indexers (3c), only for a tracker the definitions miss | Story | IDX-10 | 3 | C | L | none | Later | Hold |

### Engine

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-032 | Verify that Dismiss works for grabs missing from the engine | Spike | ENG-07 | 2 | S | XS | none | Now | Done (checked against the real engine) |
| BL-040 | Per-row priority, speed and share limits on the Downloads page | Story | ENG-11 | 2 | C | M | none | Later | Ready |
| BL-041 | Health check for torrents past their seeding goal | Task | SEED-05 | 2 | S | S | none | Next | Done (movie torrents follow the global and tracker goals; there is no per-movie rule) |
| BL-042 | Verify removing one file from a seeding torrent (priority, delete, recheck) | Spike | SEED-04 | 2 | M | S | none | Now | Ready |
| BL-043 | Verify pre-download and post-completion checks against sample files | Spike | FIL-02, FIL-03 | 2 | M | M | none | Now | Ready |
| BL-044 | Sidecar: add slot label and seeding rule fields; restore test | Story | ENG-08, NFR-03 | 2, 5 | M | M | BL-050 | Next | Ready |
| BL-045 | Verify backups include the engine profile; restore test | Spike | MIG-06 | 2 | M | S | none | Now | Done (full restore and restart not run) |
| BL-046 | Engine push channel (SSE or websocket) instead of polling | Story | ENG-18 | Later | C | L | none | Later | Ready |
| BL-047 | Engine legal notice shown once at first run | Task | SET-03 | 2 | M | S | none | Next | Done (wording is a placeholder; needs owner and legal review) |
| BL-048 | Complete Downloads settings: port, UPnP, interface binding, privacy, pre-allocation | Story | SET-04 | 2 | S | M | none | Next | Partial: port, UPnP, interface, limits, privacy, encryption done; alternative-speed schedule open |

### Versions

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-050 | Version slot schema and a default slot per item | Story | VER-01 | 5 | M | XL | BL-044 | Next | Ready |
| BL-051 | Per-slot Wanted, cutoff and upgrades | Story | VER-01 | 5 | M | L | BL-050 | Next | Ready |
| BL-052 | Download another version from Search | Story | VER-02 | 5 | S | M | BL-050 | Next | Ready |
| BL-053 | Race mode per file, with winner rules and loser cleanup | Story | VER-03, VER-04 | 5 | S | XL | BL-050, BL-093 | Next | Ready |
| BL-054 | Race groups on the Downloads page | Story | VER-03 | 5 | S | M | BL-053 | Next | Ready |
| BL-055 | Verify Emby, Jellyfin and Plex multi-version conventions | Spike | VER-05 | 5 | M | S | none | Next | Ready |
| BL-056 | Version label in file names | Task | NAM-04 | 5 | M | S | BL-050, BL-055 | Next | Ready |
| BL-059 | Verify subtitle language, forced and SDH naming, and copy numbers | Spike | NAM-02 | 2 | S | S | none | Now | Ready |
| BL-057 | Default version slots and race defaults in Settings | Story | SET-02 | 5 | S | M | BL-050 | Next | Ready |
| BL-058 | Decide the maximum number of version slots (decision 6) | Decision | VER-01 | 5 | S | XS | none | Next | Decision |

### Migration

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-060 | Library reorganisation tool with preview, journal and undo | Story | MIG-01, NFR-02, FIL-09 | 6 | M | XL | BL-010 | Later | Ready |
| BL-061 | Importer for the Sonarr database | Story | MIG-02 | 6 | M | L | BL-080 | Later | Ready |
| BL-062 | Importer for the Radarr database | Story | MIG-02 | 6 | M | M | BL-061 | Later | Ready |
| BL-063 | Importer for the Prowlarr database | Story | MIG-02 | 6 | M | M | BL-061 | Later | Ready |
| BL-064 | Importer for qBittorrent BT_backup, keeping seeding | Story | MIG-02 | 6 | M | L | BL-060 | Later | Ready |
| BL-065 | Settings review for duplication and wording | Task | UI-08 | 6 | S | M | none | Later | Ready |

### Release

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-070 | Ownership marker and start-up protection before Phase 7 | Story | MIG-05 | 1e–7 | M | M | BL-096 | Next | Decision |
| BL-071 | External contracts migration with identity records and rollback | Story | MIG-04 | 7 | M | XL | BL-070, BL-004, BL-087 | Later | Ready |
| BL-072 | Windows installer: distinct AppId, install location, verified service | Story | MIG-04, PKG-02 | 7 | M | L | BL-071 | Later | Ready |
| BL-073 | systemd unit update and Docker and macOS packaging notes | Task | MIG-04 | 7 | M | M | BL-071 | Later | Ready |
| BL-074 | Single Docker image running the host and the engine | Story | PKG-01 | 7 | M | L | BL-075 | Later | Ready |
| BL-075 | Engine CI binaries for linux-x64, linux-arm64, win-x64 and macOS | Story | ENG-16, PKG-04, NFR-08 | 7 | M | L | none | Later | Ready |
| BL-076 | Update channel and updater | Story | PKG-03 | 7 | M | L | BL-075 | Later | Ready |
| BL-077 | Bundle a definitions snapshot in each release | Task | IDX-03 | 7 | M | S | BL-075 | Later | Ready |
| BL-078 | Remove the Sonarr v3 API once the frontend no longer uses it | Task | API-02 | 7 | S | M | none | Later | Ready |
| BL-079 | Optional v3 compatibility shims for Overseerr and Jellyseerr | Story | API-03 | 7 | C | M | BL-078, BL-091 | Later | Decision |
| BL-085 | User documentation for install, migration and settings | Story | PKG-05 | 7 | S | M | BL-071 | Later | Ready |
| BL-086 | Translation project once Phase 4 is complete | Task | NFR-10 | 4+ | C | M | none | Later | Ready |
| BL-087 | Phase 7 follow-up items: legacy backup identity and rollback limits | Task | MIG-04 | 7 | S | S | none | Next | Ready |

### Decisions

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-080 | Fresh migration chain, or continue Sonarr's chain at 242? | Decision | CON-03 | 4 | M | XS | none | Now | Decision |
| BL-081 | Align the API path: /api/v1 in the roadmap, /api/v5 in code | Decision | API-01 | 2–4 | M | XS | none | Now | Decision |
| BL-090 | Decision 1: when to rename NzbDrone.* namespaces | Decision | NFR-12 | 4+ | S | XS | none | Later | Decision |
| BL-091 | Decision 3: support an external torrent client? | Decision | CON-05 | later | S | XS | none | Later | Decision |
| BL-092 | Decision 7: Plex naming template | Decision | VER-05 | 5 | S | XS | BL-055 | Next | Decision |
| BL-093 | Decision 5: how a race winner is chosen | Decision | VER-03 | 5 | S | XS | none | Next | Decision |

### Quality

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-082 | Clear the 18 baseline test failures | Task | NFR-07 | all | M | M | none | Now | Done |
| BL-083 | Confirm the CI gate: GitHub Actions or local checks only | Spike | PKG-06 | 1 | M | XS | none | Now | Ready |
| BL-084 | Browser check for each UI step (Playwright), recorded per PR | Task | UI-09 | all | S | S | none | Now | Ready |

### Process

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-088 | Set up the backlog in GitHub Projects (see 04-product-backlog.md) | Task | n/a | n/a | M | XS | none | Now | Ready |

### Owner inputs

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-094 | Obtain a TVDB v4 project key and check its licence terms | Task | MET-06 | 4 | M | S | none | Now | Blocked: owner |
| BL-095 | Obtain a TMDb key and confirm attribution | Task | MET-06 | 4 | M | S | none | Now | Blocked: owner |
| BL-096 | Decide whether the ownership marker ships in an earlier release | Decision | MIG-05 | 1e | M | XS | none | Now | Decision |

### Addendum A

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-100 | Settle the add-request and 'already in Cinnabarr' contract, shared with Addendum B | Spike | A-03, B-01 | after 4–5 | M | S | none | Future | Ready |
| BL-101 | Jellyfin server plugin: settings, user allow-list, proxy endpoints | Story | A-01 | Future | S | XL | BL-100 | Future | Future |
| BL-102 | Discover and Add web layer on jellyfin-web | Story | A-02 | Future | S | L | BL-101 | Future | Future |
| BL-103 | Test the webOS app with remote-only navigation | Task | A-04 | Future | S | M | BL-102 | Future | Future |

### Addendum B

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-110 | Home relay client in Cinnabarr, off by default, with a mock relay | Story | B-02 | Future | S | L | BL-100 | Future | Future |
| BL-111 | Vercel relay: queue, authentication, snapshot, optional metadata proxy | Story | B-01, B-03 | Future | S | L | BL-110 | Future | Future |
| BL-112 | Android APK: Discover, Requests, Library status | Story | B-05 | Future | S | L | BL-111 | Future | Future |
| BL-113 | Relay security: key rotation, expiry, rate limits, offline indicator | Task | B-04 | Future | M | M | BL-111 | Future | Future |

### Addendum C

| ID | Item | Type | Requirements | Phase | Priority | Size | Depends on | Sequence | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BL-120 | Re-run NHKollector against the live NHK site and test yt-dlp listing | Spike | C-01 | after 4 | M | S | none | Now | Ready |
| BL-121 | Create the private cinnabarr-definitions repository | Task | C-04 | Future | M | XS | none | Next | Blocked: owner |
| BL-122 | yt-dlp download engine wrapper: JSON output, progress, errors, pinned version | Story | C-02, C-03 | Future | S | L | BL-010 | Future | Future |
| BL-123 | YouTube channel source, opt-in, via Atom feed | Story | C-05, C-06 | Future | S | M | BL-122 | Future | Future |
| BL-124 | Definition loader, schema, fixtures and zero-results alert | Story | C-04, C-07 | Future | M | M | BL-121 | Future | Future |
| BL-125 | NHK World definition | Story | C-01 | Future | S | M | BL-120, BL-124 | Future | Future |
| BL-126 | Date-based matching and daily series naming | Story | C-08 | Future | S | S | BL-122 | Future | Future |
| BL-127 | Per-source legal opt-in and cookie handling | Task | C-09 | Future | M | S | BL-123 | Future | Future |

## Owner inputs needed

| ID | Item | Status |
| --- | --- | --- |
| BL-002 | Phase 1e rename to Cinnabarr (UI, strings, logos, metadata, docs) | Blocked: owner theme and icons |
| BL-004 | Decide whether to rename the repo and docs/embarr folder | Decision |
| BL-014 | Series metadata from TVDB directly, replacing Skyhook (4i) | Blocked: TVDB key and licence |
| BL-015 | Movie metadata from TMDb directly, replacing the interim service (4i) | Blocked: TMDb key |
| BL-058 | Decide the maximum number of version slots (decision 6) | Decision |
| BL-070 | Ownership marker and start-up protection before Phase 7 | Decision |
| BL-079 | Optional v3 compatibility shims for Overseerr and Jellyseerr | Decision |
| BL-080 | Fresh migration chain, or continue Sonarr's chain at 242? | Decision |
| BL-081 | Align the API path: /api/v1 in the roadmap, /api/v5 in code | Decision |
| BL-090 | Decision 1: when to rename NzbDrone.* namespaces | Decision |
| BL-091 | Decision 3: support an external torrent client? | Decision |
| BL-092 | Decision 7: Plex naming template | Decision |
| BL-093 | Decision 5: how a race winner is chosen | Decision |
| BL-094 | Obtain a TVDB v4 project key and check its licence terms | Blocked: owner |
| BL-095 | Obtain a TMDb key and confirm attribution | Blocked: owner |
| BL-096 | Decide whether the ownership marker ships in an earlier release | Decision |
| BL-121 | Create the private cinnabarr-definitions repository | Blocked: owner |
