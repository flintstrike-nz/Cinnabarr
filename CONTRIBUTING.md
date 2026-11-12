# Contributing to Cinnabarr

Cinnabarr is built in phases. The plan, design decisions and open questions are in [docs/cinnabarr/ROADMAP.md](docs/cinnabarr/ROADMAP.md).

## Before you start

- Check which roadmap phase the change belongs to. Changes for a later phase should wait or be discussed in an issue first.
- Base your branch on `develop` and open pull requests against `develop`.

## Pull requests

- Keep each pull request focused on one roadmap step.
- Describe what changed and how you tested it. Add screenshots for UI changes.
- AI-assisted contributions are welcome. You are still responsible for the change working as described.
- Every pull request is reviewed before it is merged. Respond to each finding, either with a fix or with a reason on the thread. (Automated Copilot reviews are no longer available.)

## Development setup

- Backend: .NET SDK version from `global.json`. Build with `dotnet build src/Sonarr.sln`.
- Frontend: Node.js and Yarn. Run `yarn install`, then `yarn start` for a development build that watches for changes.
- Run the checks listed in [AGENTS.md](AGENTS.md) before pushing.

## Code style

- Follow the existing style in the file you are editing. `.editorconfig`, ESLint and StyleCop settings in the repository apply.
- Use 4 spaces in C#, 2 spaces in TypeScript and CSS.
- Keep upstream-derived code recognisable where practical, so fixes from Sonarr, Radarr and Prowlarr can still be cherry-picked.

## Translations

English strings are in `src/NzbDrone.Core/Localization/Core/en.json`. Add new keys there only; other languages fall back to English.
