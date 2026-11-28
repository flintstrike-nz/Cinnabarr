# cinnabarr-play

Cinnabarr's game streamer. It runs a **libretro core** (the emulators RetroArch uses) on the server, encodes the picture with the GPU (NVIDIA NVENC, or x264 on the CPU) and sound with Opus, and sends both to a browser over **WebRTC**. Controller input comes back on a WebRTC data channel. This is for the consoles a TV can't emulate itself: PlayStation 2, GameCube, Wii, and Nintendo 64 / PlayStation on slow TVs. The plan is [docs/cinnabarr/JELLYFIN.md](../../docs/cinnabarr/JELLYFIN.md), section 3 and steps G7–G8.

**Status: G7a, the prototype.** It plays software-rendered cores (SNES, NES, Mega Drive and so on, and the test core). GPU-rendered cores (N64, PS2, GameCube, Wii) need G7c. Cinnabarr doesn't start it yet (G7b). The Jellyfin screens don't use it yet (G7d).

## Why not RetroArch itself

RetroArch is a desktop frontend: it wants a screen, and its built-in streaming goes through FFmpeg to RTMP with seconds of delay. The cores are separate libraries behind a small C API ([libretro.h](third_party/libretro/libretro.h)), so this program loads them directly. It's the same idea as Cinnabarr's torrent engine: a separate process that Cinnabarr starts, health-checks and stops.

## Build

Needs CMake 3.21+, a C++20 compiler, and FFmpeg's development libraries with x264 and Opus. NVENC also needs an NVIDIA GPU and driver at run time. You also need nlohmann-json and cpp-httplib. On Ubuntu 24.04:

```bash
sudo apt-get install cmake ninja-build g++ pkg-config libavcodec-dev libavutil-dev libswscale-dev libswresample-dev libopus-dev libx264-dev nlohmann-json3-dev libcpp-httplib-dev libssl-dev
```

```bash
cmake -S engines/cinnabarr-play -B build/cinnabarr-play -G Ninja
```

```bash
cmake --build build/cinnabarr-play
```

libdatachannel (WebRTC) is fetched and built from its v0.24.8 release by CMake. Without network access, point `-DFETCHCONTENT_SOURCE_DIR_LIBDATACHANNEL=` at a checkout of that tag that includes its submodules.

The build also makes `cinnabarr_test_libretro.so`, a tiny core for tests. It shows a dark blue screen with a moving bar, turns the screen white while any button is held, and plays a 440 Hz tone.

## Run

```bash
build/cinnabarr-play/cinnabarr-play --cores <folder with cores> --api-key-file <file> --encoder auto
```

| Option | Meaning |
| --- | --- |
| `--cores <dir>` | Cores can only be loaded from this folder. |
| `--api-key-file <file>` | The API key. A new one is written there if the file is empty or missing. |
| `--port <n>`, `--bind <address>` | Control API address. Default is any free port on `127.0.0.1`. It prints `LISTENING <address>:<port>`. |
| `--encoder auto\|nvenc\|x264` | `auto` tries NVENC, then x264. |
| `--udp-ports <a-b>` | UDP ports WebRTC may use, for publishing from Docker. |
| `--dev-page <file>` | Serve a test page at `/dev` (for example `test/dev.html`). |

### Cores

Cinnabarr doesn't ship cores (GAMES.md section 3). For trying things by hand, get one from the libretro buildbot, for example `https://buildbot.libretro.com/nightly/linux/x86_64/latest/snes9x_libretro.so.zip`, and unzip it into the cores folder. BIOS files go in the folder passed as `system`. Battery saves (`.srm`) go in `save`.

## Control API

All calls need `X-Api-Key: <key>` (or `Authorization: Bearer <key>`). Only one game runs at a time; starting another stops the first, and its battery save is written.

| Call | What |
| --- | --- |
| `GET /health` | `{ ok, version, session }` |
| `POST /sessions` `{ core, game, system?, save?, encoder?, bitrateKbps? }` | Loads the game. Returns `{ id, sdp }`, where `sdp` is the WebRTC offer with all ICE candidates. |
| `POST /sessions/{id}/answer` `{ sdp }` | The browser's answer. The game runs while a viewer is connected. |
| `GET /sessions/{id}` | Stats: encoder, size, fps, frames sent, average kbit/s, encode time, late frames, input messages |
| `GET` / `PUT /sessions/{id}/state` | The core's save state (`retro_serialize`), as raw bytes |
| `DELETE /sessions/{id}` | Stops the game and writes its battery save |

Input arrives on the data channel `input` (unordered, no retransmits). Each message is 12 bytes, little-endian:
- `u8` version (1)
- `u8` port (0–3)
- `u16` buttons, as libretro joypad bits: B 0, Y 1, Select 2, Start 3, Up 4, Down 5, Left 6, Right 7, A 8, X 9, L 10, R 11, L2 12, R2 13, L3 14, R3 15
- `i16` left x, left y, right x, right y

## The delay test (the G7a gate)

Streaming is worth building on only if the delay from pressing a button to seeing the result is low enough to play: roughly under 80 ms on the home network. To measure it on the TV:

1. On the server, run it reachable from the TV, for testing only. That means `--bind 0.0.0.0` exposes the control API (key-protected, no TLS) on the network, so stop it afterwards.

   ```bash
   cinnabarr-play --cores <cores> --api-key-file key.txt --port 47990 --bind 0.0.0.0 --udp-ports 50000-50100 --dev-page engines/cinnabarr-play/test/dev.html
   ```

2. On the TV, open the web browser at `http://<server>:47990/dev`.
3. Enter the key from `key.txt` and the full paths of `cinnabarr_test_libretro.so` and any small file as the game.
4. Press **Start**, then **Measure delay**.

The page presses a button ten times and times how long the picture takes to turn white. Add the TV's own input lag on top (about 10–20 ms in game mode). Record the result in JELLYFIN.md section 6.

Measured while building, with Chromium on the same machine, x264 on the CPU and the test core: **median 56 ms, best 39 ms** over 10 presses. That includes the page only checking the picture once per screen refresh.

## Tests

There's no unit test suite yet. The checks below were run by hand for G7a:
- the API refuses requests without the key;
- cores outside `--cores` are refused, including `..` paths;
- the offer carries H.264, Opus and the data channel;
- Chromium receives and decodes the stream with no dropped frames;
- input changes the picture;
- save states round-trip, and a bad one is refused;
- the battery save is written on stop;
- the snes9x core from the buildbot streams at 256×224 and 60.1 fps.

## Licence

GPL-3.0, like Cinnabarr. `third_party/libretro/libretro.h` is MIT. libdatachannel is MPL-2.0 and FFmpeg is LGPL/GPL depending on how it's built. Cores have their own licences and aren't shipped.
