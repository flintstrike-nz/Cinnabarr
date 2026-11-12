# Cinnabarr: Requirements Traceability Matrix (RTM)

Baseline: `origin/develop` at `8507a4c`. Links each requirement in the [SRS](01-SRS-FRD.md) to its source, phase, status, evidence, verification, business goal ([PRD](03-PRD.md)) and backlog item ([backlog](04-product-backlog.md)).

Also available as [02-RTM.csv](02-RTM.csv) for filtering.

## Summary

| Status | Count |
| --- | --- |
| Done | 50 |
| Partial | 27 |
| Open | 42 |
| Hold | 1 |
| Ongoing | 5 |
| Future | 18 |
| Total | 143 |

## Goals

| Goal | Statement |
| --- | --- |
| G1 | One library, one queue, one search, one settings tree (ROADMAP §1) |
| G2 | Never lose, hide or stop seeding a torrent; never delete a video without approval |
| G3 | Automatic, reliable and safe imports: direct to the final place, validated per file |
| G4 | Hold several versions of an item and race sources for one file |
| G5 | Follow any web source (NHK World first, YouTube next), not one hard-coded site |
| G6 | Add and browse from anywhere: TV (webOS) and phone, without opening the network |
| G7 | Migrate existing installs and rename to Cinnabarr without breaking anything |
| G8 | Own the metadata and indexer definitions; no dependency on Sonarr or Radarr services |

## Matrix

Evidence cites merged pull request numbers on `develop`, or the build note, or the code path. Verification is the check that proves the requirement. A status of Done means the build note or PR says so. Partial and Open items are the backlog's work.

| ID | Requirement | Source | Phase | Priority | Status | Evidence | Verification | Goal | Backlog |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| LIB-01 | One library for series and movies | §1, §6.1 | 4h | M | Partial | #65 merged list; bulk bar on movie list #79 only | Manual browser check; API list test | G1 | BL-022 |
| LIB-02 | Shared profiles, formats and tags | §4.1 | 4c | M | Done | #44 qualities; #48 custom formats (21 tests) | Unit tests on shared profiles | G1, G8 | none (see notes) |
| LIB-03 | Media type on shared records | §4.1 | 4d | M | Done | #51 shared RemoteMedia; #56; #68 movie history and blocklist | Unit and API tests | G1 | none (see notes) |
| LIB-04 | Library read model and projections | §4.1 | 4h | M | Done | #65 LibraryItem list; #64 movie Wanted; #72 Calendar | Projection tests; manual check | G1 | none (see notes) |
| LIB-05 | Merged Wanted page | §6.1 | 4h | S | Partial | #64 keeps a separate movie Wanted page; merged page not confirmed | Manual browser check | G1 | BL-020 |
| LIB-06 | Type-specific item pages | §6.1 | 4g | S | Partial | #62, #74, #75 movie pages; collection and credits not confirmed | Manual browser check | G1 | BL-017, BL-024 |
| LIB-07 | Import existing movie folders | §6.1 Add tab; §7 4g | 4g | S | Partial | #73 single-folder import; 'import all matched' and re-match not done | Unit tests; manual check | G3 | BL-023 |
| LIB-08 | Movie import lists | §7 Phase 4 (4f) | 4f | S | Open | Not started | Unit tests on list sources | G8 | BL-013 |
| LIB-09 | Discover in Add | §6.1 Add | 4g | S | Open | No build note mentions Discover | Manual browser check | G1, G8 | BL-018 |
| ENG-01 | Supervised built-in engine | §3 Engine lifecycle; §5 | 2 | M | Done | #9 supervisor; EngineSupervisorFixture | Supervisor tests | G2 | none (see notes) |
| ENG-02 | Loopback-only engine API | §3; §5 Binding, Preferences | 2 | M | Done | #9 | Engine configuration tests; check that other bindings are refused | G2 | none (see notes) |
| ENG-03 | One download client | §4.4; §6.3 | 2 | M | Done | #28 external clients; #29 Usenet; #30 path mappings | Unit tests; API refuses adding clients | G1 | none (see notes) |
| ENG-04 | Info-hash is the only key | §4.4 rule 1; §4.13 | 2 | M | Done | #12; #23 v2-hash ownership | Unit tests on ownership | G2 | none (see notes) |
| ENG-05 | Cinnabarr owns intent, engine owns facts | §4.4 rule 2 | 2 | M | Done | #11; #12 | Unit tests | G2 | none (see notes) |
| ENG-06 | Reconcile without deleting | §4.4 rules 3–4 | 2 | M | Done | #11; #13 flag; #14 re-add | Reconcile tests (EngineReconcileServiceFixture) | G2 | none (see notes) |
| ENG-07 | Re-add or dismiss a missing grab | §4.4 rule 3 | 2 | S | Partial | Re-add #14; Dismiss not confirmed | Manual check | G2 | BL-032 |
| ENG-08 | Sidecar recovery | §4.4 rule 5 | 2 | M | Partial | #12 base fields; slot and seeding fields not confirmed | Restore test from a backup taken before a grab | G2 | BL-044 |
| ENG-09 | Every torrent is visible | §4.4 Visibility | 2 | M | Done | #11 | UI and unit tests | G2 | none (see notes) |
| ENG-10 | Downloads page is the only download UI | §6.2 | 2 | M | Done | #25 file details; #26 controls; #27 tabs and Add Torrent | Manual browser check; API tests | G1 | none (see notes) |
| ENG-11 | Per-row priority, speed and share limits | §6.2; §7 Phase 2 status | 2 | C | Open | Left for later in Phase 2 status | Manual check | G1 | BL-040 |
| ENG-12 | Direct-to-final download | §4.8 flow | 2 | M | Done | #15 hold until whitelist; #16 direct into library | Integration test with a sample torrent | G3 | none (see notes) |
| ENG-13 | Per-file import | §4.8 Import | 2 | M | Done | #16 | Unit tests on repeated completion events | G3 | none (see notes) |
| ENG-14 | Engine-aware upgrades | §4.8 Name taken | 2 | M | Done | #17 | Unit tests | G3 | none (see notes) |
| ENG-15 | Movies download direct to the final place | §7 Phase 4 (4e-4) | 4 | M | Open | Planned in 4e-4; not built | Integration test | G3 | BL-010 |
| ENG-16 | Engine binaries built in CI | §5 CI builds; §7 Phase 7 | 7 | M | Open | Moved to Phase 7 (Phase 2 status) | Build pipeline run | G7 | BL-075 |
| ENG-17 | Engine binary name | §5; §4.7 | 1e | M | Open | #9 commit title still says cinnabarr-engine | Code search and build check | G7 | BL-005 |
| ENG-18 | Engine pushes changes | §5; §9 decision 4 | Later | C | Open | Polling in place | Latency test | G1 | BL-046 |
| FIL-01 | File whitelist | §4.8 File whitelist | 2 | M | Done | #15 | Unit tests, including movie.mkv.exe | G3 | none (see notes) |
| FIL-02 | Pre-download checks | §4.8 Checks | 2 | M | Partial | Not confirmed in build notes | Unit tests | G3 | BL-043 |
| FIL-03 | Post-completion video checks | §4.8 Checks | 2 | M | Partial | DetectSample reused by import; rejection path not confirmed | Unit tests with sample files | G3 | BL-043 |
| FIL-04 | Subtitle validation | §4.8 Subtitles | 2 | M | Done | #18 | Unit tests | G3 | none (see notes) |
| FIL-05 | Optional staging folder | §4.8 Optional staging | 2 | S | Done | #19 | Manual check | G3 | none (see notes) |
| FIL-06 | Part files cleaned | §4.8 Part files; §4.13 | 2 | M | Done | #23; #24 | Unit tests | G2 | none (see notes) |
| FIL-07 | Movie file import | §7 Phase 4 (4e-3) | 4 | M | Done | #59; #60 | Unit tests; manual import | G3 | BL-010 |
| FIL-08 | Movie extras | §4.8; §7 (4e-5) | 4 | S | Partial | #71 subtitles; other extras not done | Unit tests | G3 | BL-012 |
| FIL-09 | No video deleted without approval | §4.12; §4.13 | 2, 6 | M | Partial | Orphan cleanup #23, #24 done; reorganisation tool not started | Unit tests; approval flow test | G2 | BL-060 |
| VER-01 | Version slots | §4.1; §4.9 | 5 | M | Open | Not started | Data-model and upgrade tests | G4 | BL-050, BL-051, BL-058 |
| VER-02 | Download another version | §4.9 | 5 | S | Open | Not started | Manual check | G4 | BL-052 |
| VER-03 | Race mode | §4.9 | 5 | S | Open | Not started | Simulated race tests | G4 | BL-053, BL-054, BL-093 |
| VER-04 | Race safety | §4.9; §8 | 5 | M | Open | Not started | Unit tests | G4 | BL-053 |
| VER-05 | Multi-version naming verified | §4.11; §9 decision 7 | 5 | M | Open | Not started | Documented test against each server | G4 | BL-055, BL-092 |
| SEED-01 | Optional seeding with defaults | §4.10 | 2 | M | Done | #20 | Unit tests | G2 | none (see notes) |
| SEED-02 | Scoped seeding goals | §4.10 | 2 | S | Done | #21; #22; #25 | Unit tests; manual check | G2 | none (see notes) |
| SEED-03 | Videos never deleted for seeding | §4.10 | 2 | M | Done | #20 | Unit test | G2 | none (see notes) |
| SEED-04 | Per-file removal from a seeding torrent | §4.10 Removing one file | 2 | M | Partial | Mechanism not confirmed in build notes | Integration test on a seeding torrent | G2 | BL-042 |
| SEED-05 | Overdue seeding flagged | §4.10 Visibility | 2 | S | Open | Not confirmed | Unit test | G2 | BL-041 |
| NAM-01 | Flat library layout | §4.11 | 2, 4 | M | Done | Series via Sonarr's organiser (base); movies #57 | Unit tests on naming | G3 | none (see notes) |
| NAM-02 | Subtitle naming | §4.11 Subtitles | 2 | S | Partial | #18 registration; tag and copy-number rules not confirmed | Unit tests | G3 | BL-059 |
| NAM-03 | Multi-episode and specials naming | §4.11 | base | S | Done | Inherited from Sonarr's organiser | Existing Sonarr tests | G3 | none (see notes) |
| NAM-04 | Version label in file names | §4.11 | 5 | M | Open | Depends on VER-01 | Unit tests | G4 | BL-056 |
| NAM-05 | Movie naming settings and rename | §4.11; §4.12 | 4 | S | Partial | #66; #67 file rename; folder rename and token picker not done | Unit tests; manual check | G3 | BL-011 |
| MIG-01 | Library reorganisation tool | §4.12 | 6 | M | Open | Not started | Journal and undo tests; preview check | G7 | BL-060 |
| MIG-02 | Importers for existing installs | §4.5; §7 Phase 6 | 6 | M | Open | Not started | Import tests on sample databases | G7 | BL-061, BL-062, BL-063, BL-064 |
| MIG-03 | Orphan cleanup | §4.13 | 2 | M | Done | #23; #24 review screen | Unit tests | G2 | none (see notes) |
| MIG-04 | External contracts migration | §4.7; §7 Phase 7 | 7 | M | Open | Designed in the roadmap; not built | Migration tests on each platform; rollback drill | G7 | BL-071, BL-072, BL-073, BL-087 |
| MIG-05 | Ownership check before Phase 7 | §7 Phase 7 (earlier releases) | 1e–7 | M | Open | Not started | Start-up test with an existing Sonarr folder | G7 | BL-070, BL-096 |
| MIG-06 | Backups include the engine profile | §5 Backups | 2 | M | Partial | Not confirmed | Backup and restore test | G2 | BL-045 |
| MET-01 | Interim movie metadata | §4.14 (interim, build note 4a) | 4a | M | Done | #42 | Unit tests on the client | G8 | none (see notes) |
| MET-02 | Series metadata from TVDB | §4.14; §8 | 4i | M | Open | Skyhook still in use | Integration test; licence check | G8 | BL-014 |
| MET-03 | Movie metadata from TMDb | §4.14; §8 | 4i | M | Open | Not started | Integration test | G8 | BL-015 |
| MET-04 | IMDb identifiers | §4.14 | 4i | S | Open | Not started | Unit tests | G8 | BL-016 |
| MET-05 | Scene mappings | §4.14; §8 | 4i | M | Open | Not started | Unit tests | G8 | BL-016 |
| MET-06 | Metadata licence terms | §8 | 4i | M | Open | Not started | Checklist sign-off | G8 | BL-014, BL-017, BL-094, BL-095 |
| IDX-01 | Indexer engine core | §4.3; §7 Phase 3 (3a-1) | 3 | M | Done | #33 (103 ported tests) | Ported and new tests | G1 | none (see notes) |
| IDX-02 | Definitions from the owner's source | §4.3; §7 (3a-2) | 3 | M | Done | #34 | Update tests; Prowlarr hosts not reachable | G8 | none (see notes) |
| IDX-03 | Bundled definitions snapshot | §7 (3a-2) | 7 | M | Open | Packaging step not done | Release check | G7 | BL-077 |
| IDX-04 | Saved indexers run on the engine's HTTP stack | §7 (3b-1) | 3 | M | Done | #35; IndexerUnsupportedCheck | Tests; health check | G1 | none (see notes) |
| IDX-05 | Cardigann adapter | §7 (3b-2) | 3 | M | Done | 3b-2 (commit 8545509) | Mapper tests | G1 | none (see notes) |
| IDX-06 | Add indexers from definitions | §7 (3f-1) | 3 | M | Done | #37 | Manual check | G1 | none (see notes) |
| IDX-07 | Indexer proxies | §4.3; §7 (3d) | 3 | S | Done | #39 | API tests | G1 | none (see notes) |
| IDX-08 | Indexer stats and query history | §7 (3e) | 3 | S | Done | #40 | Tests; manual check | G1 | none (see notes) |
| IDX-09 | Indexer test and flags | §7 (3f) | 3 | S | Partial | Test and flags not confirmed | Manual check | G1 | BL-030 |
| IDX-10 | Native Prowlarr torrent indexers | §7 (3c) | 3 | C | Hold | On hold until a tracker is missing from the definitions | Per-indexer tests | G1 | BL-031 |
| IDX-11 | Search page | §6.1; §7 (3g, 4h-3) | 4 | M | Partial | #70; grabs for titles not in the library, category and indexer filters, and sorting not done | Manual check; API tests | G1 | BL-025 |
| IDX-12 | Usenet removed | §4.4; §2 | 2 | M | Done | #29 | Build check | G1 | none (see notes) |
| IDX-13 | Definition refresh schedule | §7 (3b-2) | 3 | S | Done | 3b-2 | Scheduler tests | G8 | none (see notes) |
| API-01 | One API and key | §4.6 | 2–4 | M | Partial | Built under /api/v5 (for example /api/v5/engine); roadmap says /api/v1 | API path conformance check | G8 | BL-081 |
| API-02 | Remove the v3 API | §4.6 | 7 | S | Open | Not started | Build check | G8 | BL-078 |
| API-03 | Optional v3 compatibility shims | §4.6; §9 decision 2 | 7 | C | Open | Decision pending | Compatibility test with each tool | G8 | BL-079 |
| API-04 | Release push routes movies | §7 (4g) | 4 | S | Done | Checked end to end (4g note) | API test | G1 | none (see notes) |
| UI-01 | Navigation | §6.1; §7 (1b) | 1 | M | Done | #3; Search #70 | Manual browser check | G1 | none (see notes) |
| UI-02 | Add tab | §6.1 | 4g | M | Partial | Import Movies #73; unified search and Discover not confirmed | Manual browser check | G1 | BL-018 |
| UI-03 | Merged Calendar | §6.1; §4.1 | 4h | M | Partial | #72 Calendar merged; grid filters, iCal and agenda do not include movies yet | Manual browser check; iCal test | G1 | BL-021 |
| UI-04 | Merged Activity | §6.1 Activity | 4g | M | Open | Still series-only per #68 and #69 notes | Manual browser check | G1 | BL-019 |
| UI-05 | Library bulk actions | §6.1 | 4h | S | Open | Bulk bar on the movie list only (#79) | Manual browser check | G1 | BL-022 |
| UI-06 | Movie screens rebuilt on Cinnabarr patterns | §6.4 | 4g | M | Done | #61; #62 | Lint, type check, build, browser run | G1 | none (see notes) |
| UI-07 | Frontend stack | §6.4 | base | M | Done | Inherited | yarn lint, typecheck and build | G1 | none (see notes) |
| UI-08 | Settings tree | §6.3 | 2–6 | S | Partial | Downloads and root folders in place; default slots and race defaults not yet | Manual browser check | G1 | BL-065 |
| UI-09 | Browser check for each UI step | AGENTS.md; build notes 4g-5, 4h-4 | process | S | Partial | Several steps checked with lint and build only | Browser run recorded per PR | G2 | BL-084 |
| SET-01 | Analytics off | §7 (1d); §6.3 | 1 | M | Done | #6 | Network trace | G2 | none (see notes) |
| SET-02 | Default version slots and race defaults | §6.3; §4.9 | 5 | S | Open | Not started | Manual browser check | G4 | BL-057 |
| SET-03 | Engine legal notice at first run | §5; §8 | 2 | M | Open | Not confirmed | Manual browser check | G7 | BL-047 |
| SET-04 | Downloads settings | §6.3 | 2 | S | Partial | Speed and alternative speed #26; others not confirmed | Manual browser check | G1 | BL-048 |
| BRD-01 | Phase 1 rename to Cinnabarr | §7 Phase 1 | 1 | M | Partial | #4 (1c); header and sidebar still say Sonarr (review of #10) | Manual browser check | G7 | BL-001 |
| BRD-02 | Phase 1e rename to Cinnabarr | §7 Phase 1e; owner request | 1e | M | Open | Waiting on the owner's theme and icons | Manual browser check; name check | G7 | BL-002, BL-004 |
| BRD-03 | Name clearance | §9 decision 8 | pre-release | M | Open | No search done | Documented search results | G7 | BL-003 |
| BRD-04 | Upstream namespaces kept | AGENTS.md; §4.7; §9 decision 1 | 1–4 | M | Ongoing | Kept; IndexerEngine namespace kept (#33) | Build check | G7 | none (see notes) |
| PKG-01 | Single Docker image | §3 Docker; §7 Phase 7 | 7 | M | Open | Not started | Container smoke test | G7 | BL-074 |
| PKG-02 | Desktop and Linux packages | §7 Phase 7 | 7 | M | Open | Not started | Install test on each platform | G7 | BL-072 |
| PKG-03 | Own update channel | §7 Phase 7 | 7 | M | Open | Not started | Update test | G7 | BL-076 |
| PKG-04 | Platform targets | §5 CI builds | 7 | M | Open | Not started | CI matrix | G7 | BL-075 |
| PKG-05 | User documentation | §7 Phase 7 | 7 | S | Open | Not started | Review | G7 | BL-085 |
| PKG-06 | Checks on every change | AGENTS.md checks | 1 | M | Partial | Documented in AGENTS.md; CI run status unknown | CI run or local check log | G2 | BL-083 |
| NFR-01 | Truthful state | §1 principles 2, 7, 11 | 2 | M | Done | #11; #13; #14 | Reconcile tests; manual check | G2 | none (see notes) |
| NFR-02 | File safety | §4.12; §4.13 | all | M | Done | #23; #24 | Unit tests on deletion paths | G2 | BL-060 |
| NFR-03 | Restart resilience | §4.4; §4.8 | 2 | M | Partial | Idempotent import (ENG-13); sidecar fields partial (ENG-08) | Restart test | G2 | BL-044 |
| NFR-04 | Responsive Downloads page | §5; §6.2 | 2 | S | Done | #25 (10 s); #26 (3 s) | Manual check | G1 | none (see notes) |
| NFR-05 | No telemetry | §6.3 | 1 | M | Done | #6 | Network trace | G2 | none (see notes) |
| NFR-06 | Licence | §3 Licensing | all | M | Ongoing | Inherited licence; engine kept separate | Licence review before release | G7 | none (see notes) |
| NFR-07 | Clean test baseline | AGENTS.md checks; build notes 4e-5b onward | all | M | Open | 18 known baseline failures noted in later build notes | dotnet test filter in AGENTS.md | G2 | BL-082 |
| NFR-08 | Platform support | §5 | 7 | M | Open | Not started | Platform matrix in CI | G7 | BL-075 |
| NFR-09 | Author review without Copilot | AGENTS.md; #31 | process | M | Done | #31 | Process check | G2 | none (see notes) |
| NFR-10 | Translations | §8 Translations | 4+ | C | Open | Not started | Translation project live | G7 | BL-086 |
| NFR-11 | No secrets in the product | §8 Legal; Addendum B B.5 | all | M | Ongoing | Definitions carry no credentials (IDX-02) | Release review | G8 | none (see notes) |
| NFR-12 | Upstream cherry-pick | §4.7; §9 decision 1 | 1–4 | S | Ongoing | Namespaces kept | Cherry-pick trial | G7 | BL-090 |
| IF-01 | Engine private API | §4.4; §5 | 2 | M | Done | #9; #26; Pre3 torrentscontroller.cpp | Contract test against engine build (API 2.17) | G2 | none (see notes) |
| IF-02 | Metadata providers | §4.14 | 4i | M | Open | Not started; keys needed | Integration tests with keys | G8 | BL-014 |
| IF-03 | Interim movie metadata service | §4.14 (interim) | 4a | M | Partial | #42; a third-party service the roadmap wants to replace | Client tests | G8 | BL-015 |
| IF-04 | Sonarr metadata service (Skyhook) | §8 | base | M | Partial | Still in use | Remove after MET-02 | G8 | BL-014 |
| IF-05 | Indexer definitions fork | §4.3; §7 (3a-2) | 3 | M | Done | #34 | Update tests | G8 | none (see notes) |
| CON-01 | Two processes | §3; §5 | all | M | Done | Architecture in place (#9) | Architecture review | G2 | none (see notes) |
| CON-02 | Database choice | §4.5 | all | M | Ongoing | Sonarr base supports both | Migration tests on both | G7 | none (see notes) |
| CON-03 | Migration chain | §4.5; build note 3b-1 | 4 | M | Open | Conflict between §4.5 and built code | Owner decision | G7 | BL-080 |
| CON-04 | No Usenet | §1 principle 6 | all | M | Done | #29 | Build check | G1 | none (see notes) |
| CON-05 | Torrent-only engine scope | §1; §9 decision 3 | all | M | Done | #28 | Build check | G1 | BL-091 |
| A-01 | Jellyfin server plugin | Addendum A.3 | Future | S | Future | Not started | Plugin tests on a Jellyfin release | G6 | BL-101 |
| A-02 | Discover and Add web layer | Addendum A.3 | Future | S | Future | Not started | Test on a webOS TV | G6 | BL-102 |
| A-03 | Add and status contract | Addendum A.4; B.4 | Future | S | Future | Not started | Contract tests | G6, G8 | BL-100 |
| A-04 | webOS remote-only use | Addendum A.6 step 4 | Future | S | Future | Not started | Manual test on webOS | G6 | BL-103 |
| B-01 | Vercel relay queue | Addendum B.2 | Future | S | Future | Not started | Relay contract tests | G6 | BL-100, BL-111 |
| B-02 | Home relay client | Addendum B.3 | Future | S | Future | Not started | Mock relay tests | G6 | BL-110 |
| B-03 | Status snapshot and offline view | Addendum B.2, B.6 | Future | S | Future | Not started | Offline test | G6 | BL-111 |
| B-04 | Security of the relay | Addendum B.5 | Future | M | Future | Not started | Security review | G6 | BL-113 |
| B-05 | Android APK | Addendum B.3 | Future | S | Future | Not started | Device test | G6 | BL-112 |
| C-01 | NHK World listing | Addendum C.3, C.10 | Future | S | Future | Not started (NHKollector last ran mid-2025) | Live site test | G5 | BL-120, BL-125 |
| C-02 | yt-dlp download engine | Addendum C.6 | Future | S | Future | Not started | Wrapper tests | G5 | BL-122 |
| C-03 | Verify downloads | Addendum C.6 | Future | S | Future | Not started | Test with truncated files | G5, G3 | BL-122 |
| C-04 | Private definitions repository | Addendum C.4 | Future | S | Future | Repository not created yet | Loader test against fixtures | G5 | BL-121, BL-124 |
| C-05 | YouTube source | Addendum C.3, C.8 | Future | S | Future | Not started | Source test | G5 | BL-123 |
| C-06 | Live and short filters | Addendum C.8 | Future | C | Future | Not started | Source test | G5 | BL-123 |
| C-07 | Zero-results alert | Addendum C.7 | Future | M | Future | Not started | Test with an empty listing | G5 | BL-124 |
| C-08 | Date-based matching | Addendum C.5 | Future | S | Future | Not started | Matching tests | G5 | BL-126 |
| C-09 | Opt-in and cookies | Addendum C.8 | Future | M | Future | Not started | Settings review | G5 | BL-127 |

## Discrepancies linked to requirements

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
