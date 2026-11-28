# Cinnabarr plugin for Jellyfin

Lets people use Cinnabarr from the Jellyfin apps (the goal is the LG webOS app): browse and request shows and movies, manage downloads, run interactive search and play games. The design and the steps are in [docs/cinnabarr/JELLYFIN.md](../../docs/cinnabarr/JELLYFIN.md).

**Status: J2, the skeleton.** It holds the Cinnabarr address and API key, decides what each Jellyfin user may do, tests the connection, and has the *Cinnabarr TV check* page. The screens on the TV come in later steps.

## Build

Needs the .NET SDK from the repository's `global.json` (10.x) and `zip`.

```bash
./build.sh
```

```bash
./build.sh 10.11
```

The first builds for Jellyfin 12.x, the second for Jellyfin 10.11. Each writes `out/cinnabarr_<version>_jf<target>.zip` and `out/manifest.json` (the manifest describes the last build).

Check that the page scripts still run on the TV's old web engine (from the repository root, after `yarn install`):

```bash
node clients/jellyfin-plugin/check-es2019.cjs
```

Tests:

```bash
dotnet test clients/jellyfin-plugin/tests/Jellyfin.Plugin.Cinnabarr.Tests
```

## Install

Either:

- **By hand:** unzip the zip into `<Jellyfin data folder>/plugins/Cinnabarr_0.1.0.0/` and restart Jellyfin. In Docker that is usually `/config/plugins/` inside the container.
- **From a repository:** put `manifest.json` and the zip somewhere Jellyfin can download from. Build with `CINNABARR_PLUGIN_BASE_URL=<that address> ./build.sh` so the manifest points at the zip. In Jellyfin, go to *Dashboard → Plugins → Repositories*, add the manifest's address, then install *Cinnabarr* from the catalogue.

## Set up

1. *Dashboard → Plugins → Cinnabarr*. Enter the address Jellyfin uses to reach Cinnabarr (for example `http://localhost:8989`) and Cinnabarr's API key (*Settings → General → Security* in Cinnabarr), then press **Test connection**.
2. Tick what each user may do. Administrators can do everything; anyone not ticked sees nothing from Cinnabarr.
   - **Request:** browse, search and request.
   - **Manage:** also downloads, interactive search and deleting.
   - **Play:** games.
3. Save.

The API key is stored only in the plugin's settings on the Jellyfin server, which only administrators can read. The apps only ever call this plugin's `/Cinnabarr` endpoints.

## TV check

On the TV, open the Jellyfin app, sign in as an administrator, and go to *Dashboard → Cinnabarr TV check*. Pair the controller and press a few buttons on it and on the remote (including Back). The page answers the questions in JELLYFIN.md section 6: whether the controller is seen, whether streaming (WebRTC with H.264) can work, whether Jellyfin's own controller handling is on, and which remote keys arrive. **Copy report** puts the answers on the clipboard; they are also printed at the bottom of the page.

## Endpoints

| Call | Who | What |
| --- | --- | --- |
| `GET /Cinnabarr/Me` | any signed-in user | `{ Roles: [...], Configured }`: what this user may do |
| `POST /Cinnabarr/Test` | administrators | Tests `{ Url, ApiKey }`, or the saved settings when both are empty |

## Licence

GPL-3.0, like Cinnabarr. The Jellyfin packages it builds against (`Jellyfin.Controller`, `Jellyfin.Model`) are GPL-3.0-only.
