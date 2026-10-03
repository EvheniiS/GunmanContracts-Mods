# VR settings / holster adjustment test pass

Test builds: Mod Settings 0.4.0 and VR Holster Customization 0.2.0. Build and pure-logic tests cannot verify the
Unity hand rig or controller alignment; do this pass in the headset before release.

## Settings board

- Open from phone and Ctrl+M. The initial touch/held trigger must not immediately change a row.
- Aim at the board with each empty hand: check native pointing pose, fingertip laser origin, hover, single trigger
  clicks, and accurate selection at 0.5–3 m. Holding trigger must not repeat. Test the main menu and The Range.
- Poke a row from the front. Sliding along the board and approaching from behind must not trigger adjacent rows.
- Aim/grip the top bar, lower the board toward the floor, tilt it toward your face, and release. Check near-hand
  movement too. Values must stay unchanged while moving. Stand upright: the board must remain open.
- Look at the holsters and adjust via the laser. Walk with the stick and snap turn: the board follows the rig.
- Aim away, close, grab a weapon, pause/unpause, and change scenes: no lingering pointing pose or laser. A held weapon
  must retain its pose and trigger behaviour. Close/reopen recentres the board.
- Change a bool, number, colour and choice. Confirm CHANGED/yellow, the declared default, and per-row Reset. Reset
  must update the actual mod, restore DEFAULT, and survive restart. Test a mod-managed saved-state row: no Reset.
- Compare desktop mode: each changed row has Reset, values save, cursor/weapon controls restore on close.

## Section list (1.1.0)

- Press List, then Back; press the section name: same list. Sections are alphabetical, current one red, changed ones
  yellow. Pick one far down (VR Holsters): it opens on that page and no row under the finger gets pressed.
- `X` closes from the list; reopening starts on the settings view. `<` / `>` from the list switch section and close it.
- `PanelDistance` 1.4: the board opens ~1.4 m away at the same angle below the eyes; laser hover and press work.
- Flat (Ctrl+M): List / title → click a section → lands there; wheel pages the list if it doesn't fit.

## Updated defaults (1.2.0)

**Real update test (Daredevil 1.0.0 -> 1.1.0).** DLLs staged in `feature/defaults-test/1.0.0/` and `1.1.0/`. The only
default that changed between them is `BillyClubs.ChestStunSeconds` (3 -> 1), so one round tests one case:

1. Game closed: delete `UserData/ModSettings_defaults.txt`, copy `1.0.0/Daredevil.dll` into `Mods/`.
2. Start: board → BillyClubs → `ChestStunSeconds`. Round A: **Reset** (3 = old default, "untouched").
   Round B: set it to 2 ("customised"). Quit.
3. Copy `1.1.0/Daredevil.dll` into `Mods/`. Start, open the board: it opens on **Updated defaults**, description
   says `Updated: Daredevil 1.0.0 -> 1.1.0`. A: value is now 1, row has **Revert**. B: value still 2, **Use new / Keep**.

Or simulate one by editing `UserData/ModSettings_defaults.txt` with the game closed.

- First start: the file appears, the log says `recorded the defaults of N settings`, no page, no notice.
- **Untouched:** set `BillyClubs.ChestStunSeconds` to `3` in the cfg and in the record file. Start: log line
  `default 3 -> 1; ... now uses the new one`, the cfg value is 1. The board opens on **Updated defaults** (first in
  List, yellow) with the row in blue; **Revert** puts 3 back and the row reads Done. Next open: starts on your last page.
- **Customised:** pick another number entry, set its record-file default to something else and its cfg value to a
  third value. Start: listed with **Use new** and **Keep**; the board opens on the page every time until decided.
  Restart without deciding: still listed. **Keep** → value unchanged, gone after restart. **Use new** → new default.
- Changing that setting in its own section (steps or Reset) also counts as a decision (row reads Done / changed).
- Press a row name: opens its own section with it selected and scrolled to. **Use all new** handles every waiting row.
- Change `@Daredevil` in the record file: the description shows `Updated: Daredevil x -> 1.1.0`.
- Flat (Ctrl+M): same page, rows, Keep / Use new / Revert, Use all new.

## Direct holster adjustment

- Enable VR Holsters: move by hand. Check blue markers for each empty hip, knife and back socket. An occupied socket
  or occupied hand must not enter adjustment.
- Hold grip + trigger near a slot. Release before two seconds or leave its radius: no movement or new saved offsets.
  Approach with buttons already held: no activation. Release and retry: it should work.
- Hold for two seconds: amber pulses become green with a stronger pulse. Move vertically, sideways and in depth;
  there must be no jump at activation. Release either button and check that the values persist after a level reload.
- Walk/snap turn while holding: moving the body alone must not change the calibration. Check both hands, then try
  touching two different slots at once: only one adjustment should run.
- Pause, disable adjustment or change scene during a drag: marker/gesture clears and the final offset is saved.
- With the menu still open, confirm offsets update there and per-row Reset returns sockets to the expected position.
- Holster/draw every weapon after movement. Confirm physical sockets and holograms agree; repeat with nonzero AllUpCm.
- Verify Weapon Framework/Daredevil items still dock and draw normally. Their custom back-slot positions are separate.

Do not ship personal MelonPreferences.cfg with a release. The DLL uses declared defaults; this build adds no user
configuration to its output. Run the release audit separately when preparing a Nexus upload.

0.4.1 / 0.2.1 regression: reset holster Color to Default, cycle to red and back, verify native normal/hover colors and no stale red swatch. Prepare both pistols in The Range, enter a contract, draw the left pistol and reholster knives, then return through the main menu and restart. Both prepared pistol slots must restore. Headset verification pending.

0.4.2: change settings on two scroll pages in one category; Reset section must restore both pages, leave other categories and managed saved state unchanged, and persist after restart. Verify individual row Reset still changes only that row, in VR and flat mode.
