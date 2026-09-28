# Mod Settings: Nexus upload sheet

**Status: release candidate (0.2.2), not released.**

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

Went into `release/NEXUS_TEMPLATE.txt`: every future mod's Settings section links here
(`[url=.../mods/{{ModSettingsID}}]Mod Settings[/url]`) as the optional live-editing alternative
to hand-editing the cfg. **The `{{ModSettingsID}}` placeholder needs the real mod ID filled in
across the template once this is actually uploaded** — grep `release/*/NEXUS_DESCRIPTION.txt` for
`ModSettingsID` afterward (none yet reference it; this is the first upload).

## Before uploading

- [ ] Play-test: open/close (tile and Ctrl+M), switch mods, step a number through all four
      button sizes, toggle a switch, cycle a choice and a colour, Reset, and confirm the change
      lands in `MelonPreferences.cfg` a second later.
- [ ] Confirm the phone tile behaves correctly while a Data Breach holder occupies the middle
      slot (tile should move to the empty slot and return after).
- [ ] Decide it feels solid enough to publish (README already says "board tested and works" —
      confirm that's still current, not stale from an earlier version).
- [ ] Tag: `git tag modsettings-v0.2.2` and push the tag.
- [ ] README status → released, with the Nexus link.
- [ ] Once uploaded, fill in the real `{{ModSettingsID}}` in `release/NEXUS_TEMPLATE.txt` and
      backfill the Settings-closer line into already-published mods' descriptions (optional,
      but keeps existing pages consistent with new ones).
