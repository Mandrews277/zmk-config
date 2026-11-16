# Keymap Overlay

Small, semi-transparent, click-through windows (one per half) that show the
keymap of your **current layer**. Portable Windows `.exe`: no install, no admin.

## How it works

ZMK doesn't tell the computer which layer is active, so the firmware module in
this repo (`src/layer_keys.c`, enabled with `CONFIG_ZMK_LAYER_KEYS=y` in
`comet_right.conf`, the central half) **taps F13 + layer number** whenever the
layer changes (layer 0 = F13, 1 = F14, ...). Those are ordinary key presses, so
this works over **Bluetooth** as well as USB.

The app sees those keys through a per-user keyboard hook, which needs no admin
rights, and hides them from every other app. The keymap itself is read from
`config/comet_right.keymap` on GitHub, so the overlay updates when you push
keymap changes. A cached copy is kept in `cache\` for offline use.

## Setup

1. **Flash the new firmware** (right half, the central). Push this branch; the
   normal *Build* workflow produces the `.uf2`.
2. **Get the exe** using either option:
   - Download the `KeymapOverlay` artifact from the *Build keymap overlay
     (Windows)* workflow run on GitHub, **or**
   - copy the `keymap-overlay` folder to the laptop and double-click
     `build.bat`. It compiles with the C# compiler that's already part of
     Windows. This is handy if downloaded `.exe` files are blocked.
3. Put `KeymapOverlay.exe` in any folder you can write to (e.g.
   `Documents\KeymapOverlay`) and run it. A tray icon appears. To start it with
   Windows, put a shortcut to it in `shell:startup`.

## Using it

- **Popup mode** (default): the overlay fades in when you leave the base
  layer and fades out when you come back.
- **Always show**: left-click the tray icon (or tray menu → *Always show*).
- Tray menu → **Show layer** lets you preview any layer without the keyboard.
- Tray menu → **Unlock to move windows**: drag each half where you want it,
  then lock again. Positions are saved.
- Highlighted keys switch layers. Dimmed keys are `&trans` and show what falls
  through from the base layer. Small text under a label is the hold action.

## Settings

`keymap-overlay.ini` is created next to the exe on first run (tray →
*Edit settings...*, then *Reload*). Options include `mode`, `popupSeconds`
(auto-hide timer), `opacity`, `keySize`, the keymap/layout source (URL or
local file), and `firstKey` (must match `CONFIG_ZMK_LAYER_KEYS_BASE_USAGE`).

## Notes

- Max 12 layers (F13–F24).
- On Linux/macOS the F13+ taps are harmless but may show up as
  `XF86Tools`/`XF86Launch5`... keysyms. They're unbound by default.
