# Mod Settings: Nexus upload sheet

**Status: 0.2.2 released; 0.3.0 ready (see above).** https://www.nexusmods.com/gunmancontractsstandalone/mods/28

## ★ Update to 0.3.0: flat-screen menu (users asked for it)

**Status: release files ready, NOT uploaded.** Game page → Mod Settings → Manage → Files → Upload.

| Field | Value |
|---|---|
| File | `ModSettings-0.3.0.zip` (contains `Mods/ModSettings.dll`) |
| File name | `Mod Settings` |
| Version | `0.3.0` |
| Category | Main files; move 0.2.2 to Old files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x. Now also works in flat mode (Ctrl+M).` |

`ModSettings.dll` 46,592 bytes, sha256 `9aacc571b00418de62dba979e7b9679e6642e2485b659d6338654d1ad46f5689`
(= the DLL deployed in the game folder).

Also set the **mod version** on the Details tab to `0.3.0`.

**Description:** paste [`NEXUS_DESCRIPTION.txt`](NEXUS_DESCRIPTION.txt) again (BBCode). What changed: a flat-mode line in
the intro, a *Flat mode (no headset)* section, a Ctrl+M focus tip under *Not working?*, and `v0.3.0` in the install check.

**Summary (optional, 342 chars):**
`A settings menu for every MelonLoader mod. Reads every category straight from MelonPreferences, so any mod using it gets a page automatically. In VR: press the Mod Settings tile on the phone (or Ctrl+M) and poke the board with your fingertip. Flat: Ctrl+M opens it on screen for the mouse. Changes numbers, switches, choices and colours live.`

**Changelog (Manage → Changelogs, version `0.3.0`):**
```
Flat mode support: in the flat (non-VR) version, Ctrl+M opens the same settings menu on screen, used with the mouse (wheel scrolls). While it's open the cursor is free and mouse look / shooting pause; the game itself keeps running. VR is unchanged.
```

**Reply to the users who asked for it (optional):**
```
Added in 0.3.0: in the flat version press Ctrl+M and the menu opens on screen, clickable with the mouse. The game keeps running while it's open, so open it somewhere safe. Thanks for asking!
```

### Before uploading 0.3.0

- [ ] Flat test: Ctrl+M in The Range → menu centred, cursor visible, look + fire blocked, WASD walks; change a value
      (log line, cfg saved); close → mouse look works again. Also Ctrl+M in the main menu.
- [ ] VR regression: phone tile and Ctrl+M still open the board.
- [ ] Merge `dev` → `main` (or cherry-pick the Mod Settings commit), tag `modsettings-v0.3.0`, push.

---

## First release (0.2.2)

Game page: Gunman Contracts - Stand Alone (`nexusmods.com/gunmancontractsstandalone`) → Upload a mod.

| Field | Value |
|---|---|
| Mod name | `Mod Settings` |
| Version | `0.2.2` |
| Author | `Evgeeso` |
| Category | Gameplay (same as the other mods) |
| Language | English |
| Summary (max 350 chars) | `An in-VR settings board for every MelonLoader mod. Reads every category straight from MelonPreferences, so any mod using it — mine and other people's — gets a page automatically. Take out the phone, press the Mod Settings tile (or Ctrl+M), poke with your fingertip to change numbers, switches, choices and colours live.` |
| Description | paste [`NEXUS_DESCRIPTION.txt`](NEXUS_DESCRIPTION.txt) (BBCode) |
| Tags | VR, Utility, UI, Gameplay |
| Requirements | Off-site: `MelonLoader 0.7.x`, `https://github.com/LavaGang/MelonLoader/releases`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as the other mods (source is MIT on GitHub) |

## File

| Field | Value |
|---|---|
| File | `ModSettings-0.2.2.zip` (contains `Mods/ModSettings.dll`) |
| File name | `Mod Settings` |
| Version | `0.2.2` |
| Category | Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |

`ModSettings.dll` 39,936 bytes, sha256 `6661f70ca2ee59c9228ee9df2dff8977a0b601146b40cee934722ac0d0a1ffbd`
(= the built `release/ModSettings/ModSettings.dll`; not yet deployed to a live game folder — deploy
and re-hash before uploading, so this line always matches what a downloader actually gets).

## Images

Artwork from `VirtualDesktop.Android-20260928-100919.jpg`, showing the board on the Fire Selector
page and the phone tile:

- **Thumbnail (1600 × 900):** [`ModSettings-thumbnail.jpg`](ModSettings-thumbnail.jpg), with
  only the `MOD SETTINGS` title in the same ivory/amber style as the other mod thumbnails.
- **Header (1300 × 372):** [`ModSettings-header.jpg`](ModSettings-header.jpg), with the same
  title on one line.

The prepared image bases and [`make_nexus_art.py`](make_nexus_art.py) are here for export size
adjustments. A short demo clip can still show opening the board, switching mods, changing a
number and a switch, and resetting a non-default setting.

## Downstream template change

`release/NEXUS_TEMPLATE.txt`'s Settings-closer line and link table now point at
`https://www.nexusmods.com/gunmancontractsstandalone/mods/28` directly. **Still open:** backfill
that real link into the already-published mods' descriptions (Better Bow, Fire Selector, Knee
Shot Stun predate the Mod Settings release and still link GitHub in their Settings closer) —
optional, but keeps existing pages consistent with new ones.

## Before uploading (done)

- [x] Decide it feels solid enough to publish (README says "released").
- [x] README status → released, with the Nexus link.
- [ ] Tag: `git tag modsettings-v0.2.2` and push the tag.

## Open items (not release blockers, worth doing eventually)

- [ ] Play-test: open/close (tile and Ctrl+M), switch mods, step a number through all four
      button sizes, toggle a switch, cycle a choice and a colour, Reset, and confirm the change
      lands in `MelonPreferences.cfg` a second later.
- [ ] Confirm the phone tile behaves correctly while a Data Breach holder occupies the middle
      slot (tile should move to the empty slot and return after).
