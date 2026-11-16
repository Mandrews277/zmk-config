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

**Click the tray icon** to open **Settings**. Changes apply immediately, and
the overlay stays visible while the window is open so you can see them.

- **When to show**:
  - *Pop up* (default) shows the overlay when you leave the base layer.
  - *Always show* keeps it on screen all the time.
  - *Hide after N seconds* auto-hides a popup. 0 means it stays until you're
    back on the base layer.
  - *Keep showing for N seconds after returning to base* keeps the overlay up
    briefly after you let go of a layer key.
- **Appearance**: opacity and key size.
- **Position**: click *Move windows*, drag each half anywhere (even onto
  different monitors), then click *Done moving*. If a monitor is disconnected,
  a half that was on it moves back to the main screen.
- **Keymap**: where the keymap and layout come from (URL or file).
  *Reload keymap* fetches them again after you push changes.
- **Start with Windows**: uses a per-user registry entry, so no admin is needed.

Right-click the tray icon for quick actions: *Always show*, *Show layer*
(preview any layer), *Move windows*, *Reload keymap*, *Exit*.

Highlighted keys switch layers. Dimmed keys are `&trans` and show what falls
through from the base layer. Small text under a label is the hold action.

Everything is stored in `keymap-overlay.ini` next to the exe.

## Notes

- Max 12 layers (F13–F24).
- On Linux/macOS the F13+ taps are harmless but may show up as
  `XF86Tools`/`XF86Launch5`... keysyms. They're unbound by default.
