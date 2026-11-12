# AGENTS.md

Guidance for AI coding agents working in the Cinnabarr repository (the GitHub repositories are still named EmbarrPre1 to EmbarrPre4; renaming them is the owner's step).

## Project

Cinnabarr (earlier working name: Embarr) merges Sonarr (this repo's base), Radarr, Prowlarr and a headless qBittorrent engine into one app. The design and phase plan live in [docs/cinnabarr/ROADMAP.md](docs/cinnabarr/ROADMAP.md). Read the section for the phase you are working on before changing code.

## Working rules

- AI-assisted commits and pull requests are allowed in this repository.
- Keep each pull request to one roadmap step. Split large phases into several PRs.
- Every PR gets a careful review of its diff before it is merged. Copilot reviews are no longer available, so AI-assisted PRs are reviewed by their author: correctness, safety for users' files, and the repo's own checks (build, tests, lint, typecheck). Address each finding a reviewer leaves, or explain on the thread why not.
- Run the checks below before pushing. GitHub Actions may not run on this repository, so local checks are the gate.
- The product is named Cinnabarr and the UI strings, the engine (`cinnabarr-engine`, `CINNABARR_ENGINE_*`) and the docs use that name. The repositories are still named EmbarrPre1 to EmbarrPre4 until the owner renames them.
- Keep `NzbDrone.*` namespaces and project names until the roadmap says otherwise, so upstream fixes can still be cherry-picked.
- Don't rename external contracts (database file, AppData folder, service name, `SONARR__` environment variables, custom-script variables) without the migration defined in Phase 7 of the roadmap (§4.7 explains which contracts are covered).

## Checks

Frontend:

```bash
yarn install --frozen-lockfile
yarn lint
yarn typecheck
yarn build
```

Backend (.NET SDK version from `global.json`):

```bash
dotnet build src/Sonarr.sln
dotnet test src/NzbDrone.Core.Test --filter "TestCategory!=ManualTest&TestCategory!=WINDOWS&TestCategory!=IntegrationTest&TestCategory!=AutomationTest"
```
