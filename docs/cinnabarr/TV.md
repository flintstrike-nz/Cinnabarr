# Playing games on an LG TV (webOS)

Tested on paper for: LG OLED55A1PVA (2021, webOS 6, firmware 03.53.45 / 6.5.3-47), Xbox Elite Series 2 and a generic Switch Pro controller. **Nothing here has been run on a real TV yet**: it was built and tested in a desktop browser with a simulated controller. Expect a first round of fixes after the first real try.

## What exists

- **`http://<your-pc>:8989/tv`** is a TV launcher: big game tiles you move around with the controller (d-pad or left stick), bottom face button to play, right face button to go back, LB / RB to change platform. Holding **Select + Start** for 1.5 seconds, or pressing the remote's **Back**, leaves a game. It is a plain page that works in an old browser (it is checked to be plain ES2019, because the main Cinnabarr interface needs a newer browser than webOS 6 has).
- **`http://<your-pc>:8989/tv?debug=1`** adds a panel that shows what the TV reports for your controller (its name, whether it is the standard layout, which buttons are pressed). **Use this first**: if it shows nothing when you press buttons, the TV's browser is not seeing the controller and nothing else will work.
- **The Cinnabarr Games app** (`distribution/webos`) is a tiny app that opens that page from the TV's launcher.

## Try it in this order

1. **TV web browser (nothing to install).** Open the TV's *Web Browser* app, go to `http://<your-pc>:8989/tv?debug=1` (use the PC's address on your network, not `localhost`), pair a controller (Settings → Connection → Bluetooth, or plug it in with USB) and press buttons. Bookmark the page. If the panel shows the controller, press **A** on a Master System or SNES game.
2. **The launcher app (optional, for a launcher icon).** Needs the LG *Developer Mode* app and a free LG developer account:
   1. On the PC: `cd distribution/webos && ./build.sh http://<your-pc>:8989/tv` (needs Node.js). It writes `out/org.cinnabarr.games_1.0.0_all.ipk`.
   2. Install LG's *Developer Mode* app from the TV's content store, sign in, switch *Dev Mode* and *Key Server* on.
   3. Install the app with LG's tools: `npx @webos-tools/cli ares-setup-device`, then `ares-novacom --device tv --getkey`, then `ares-install --device tv out/*.ipk` (or use the free *webOS Dev Manager* desktop app, which does the same with buttons).
   4. **A sideloaded app on a stock TV only works while Developer Mode is on, and Developer Mode switches itself off after about 50 hours unless you open the Developer Mode app and extend it.** That is an LG limit, not Cinnabarr's. If that is too fiddly, use step 1 and a bookmark.

## About the Jellyfin app

The Jellyfin app for LG is a thin shell around Jellyfin's own web page; Cinnabarr cannot add a Games page inside it. A Jellyfin *plugin* could add a menu link to `/tv`, but that does not make games run inside Jellyfin's player, and I could not confirm that Jellyfin's page reads gamepads. So the plan is the separate launcher above (and, optionally, a Jellyfin plugin that just links to it, later).

## The debug panel shows no controller

Open `/tv?debug=1` again after updating, **press a button on the controller** (a browser only lists a controller after a button press), and read the panel:

| Panel says | It means | Next step |
| --- | --- | --- |
| `Gamepad API: MISSING` | This TV browser has no gamepad support at all | Games cannot be played with a controller in it; try the sideloaded app, or a keyboard/remote |
| `Gamepad API: present, 4 slots, 0 with a controller` and `last keys seen` stay empty when you press controller buttons | The TV keeps the controller for itself and does not pass it to web pages | Try a USB cable instead of Bluetooth, or a different browser/app on the TV |
| `last keys seen` shows arrow keys / Enter / Back when you press the controller | The TV turns the controller into a remote | Menus will work, but games cannot read the controller; a USB-wired controller may be handled differently |
| A controller appears with `mapping: ""` | It works but with a non-standard button layout | Tell me the pressed button numbers and I will add a mapping |

## Things that may not work, and what to do

| Problem | Likely cause | What to try |
| --- | --- | --- |
| The debug panel never shows the controller | The TV browser does not expose Bluetooth controllers to web pages, or it did not pair | Connect the controller with a USB cable. The Elite Series 2 is reported to work wired when Bluetooth mapping is wrong |
| Buttons do the wrong thing | The controller reports a non-standard layout (the panel shows `mapping: ""`) | Tell me what the panel shows when you press each button and I will add a mapping |
| A game is slow or stutters | webOS 6's browser is old; the emulator falls back to older, slower builds | Master System and SNES should be fine; PlayStation may not be |
| PlayStation does not start | It needs your own BIOS at `config/bios/psx.bin` | See the Games section of the user guide |
| PlayStation 2 | No browser emulator exists for it | Not playable on the TV either |
| Back on the remote closes the app instead of leaving the game | webOS handles Back itself in some app types | Hold Select + Start instead |
