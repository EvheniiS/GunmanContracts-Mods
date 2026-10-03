# Mod Settings 1.2.1

Tested: 1.0.0

An in-VR settings board for every MelonLoader mod in Gunman Contracts, with a mouse-driven on-screen version for
flat mode. It reads all categories from
`MelonPreferences`, so every mod that uses MelonPreferences (ours and others) gets a page without doing anything.

## Use

- **Open / close:** take out the phone and press the **Mod Settings** tile in the middle row (an empty slot), or press
  **Ctrl+M**. The board opens `PanelDistance` in front of your eyes (default 40 cm), above the phone, and moves with you when you move with the
  stick. It closes on X, on the tile again, on a
  scene load, or if you walk 5 m away.
- **Move / tilt:** grip the top bar with an empty hand, nearby or with the laser aimed at it. Move and rotate your
  hand, then release grip to leave the board there. It stays relative to your player rig, including near the floor.
  Close and reopen to bring it back in front of your eyes.
- **Point and press trigger** with either empty hand to select from up to 4 m away, or press with an index fingertip,
  coming in from the front (sliding across the board doesn't press). Hands aimed at or near the board use the game's
  menu pointing pose. Held weapons keep their own pose. Release the opening gesture before interacting.
- **Section list:** press **List** (or the section name) at the top for a grid of every section, alphabetical,
  column by column. The current one is red, sections with changed values have yellow names. Press one to jump
  straight to it; **Back** returns. With more than 30 sections, `<` / `>` page the list while it is open.
- `<` / `>` at the top switch mods (alphabetical order); `Up` / `Down` scroll a mod's settings; poke a setting's **name** to read its
  description, default and restart note; `Reset` puts the selected setting back to its default.
- Numbers get `-big -small value +small +big` steps (from the default: 18 → 1/10, 0.6 → 0.01/0.1). Switches toggle.
  Choices written in the description (`TipFirst (...), Natural (...) or SpinEnd (...)`) and `#RRGGBB` colours cycle
  with `<` `>`. Other text (keys, paths, holster positions) is read-only here; edit it in the cfg.
- Each row shows **DEFAULT / CHANGED**, the declared default, and its own **Reset** button when changed. Yellow
  names mark changed values. Mod-managed saved state remains protected from resetting; ordinary read-only text can
  be reset to its declared default. `*` = the description says it needs a game restart.
- The VR board is now **72 × 56 cm** at scale 1 (previously 48 × 44), with larger controls and a dedicated reset column.

### Updated defaults (mod updates)

MelonLoader saves every value to the cfg, so a mod update that changes a default would never reach you. Mod Settings
remembers each setting's default in `UserData/ModSettings_defaults.txt` (delete it to start over). When an update
changes one:

- if you had left it at the old default, it moves to the new one at start (**Revert** undoes that);
- if you had changed it, your value stays until you press **Use new** or **Keep**.

Both are listed on the **Updated defaults** page, which comes first and which the board opens on while a decision is
waiting. Press a name to adjust it in its own section; **Use all new** takes the new default for every waiting row.
The first run only records the defaults, so changes are shown from the next mod update on. Settings the mod manages
itself are skipped.

**Removed settings file (1.2.1).** The record also keeps how many settings you had changed per mod when the game last
ran. If a mod had two or more changed settings and now has none (you deleted `MelonPreferences.cfg`, or its section),
the log warns "Settings were reset: ..." and the board opens on the **Updated defaults** page with that message once
(it is kept until the board has shown it). One setting reset by hand does not count.

### Flat mode (no headset)

**Ctrl+M** opens the same menu in the middle of the screen; use it with the mouse (wheel scrolls). Same pages and
buttons as the board: click a name for its description, `<` `>` for mods, **List** (or the title) for the section grid, `X` or Ctrl+M closes. While it's open the
flat controller's cursor lock is released, so mouse look and firing stop and the cursor shows; walking still works and
the game is not paused. Closing locks the cursor again (unless the game's own menu is open). Flat = no headset running
(`XRSettings.isDeviceActive` false), or the flat controller is up and no VR hands exist.

Every change is logged (`Category.Entry: old -> new`), applied to the entry at once (it fires the entry's
`OnEntryValueChanged`) and saved to `UserData/MelonPreferences.cfg` a second later.

**Live or not is up to each mod:** it applies at once if the mod reads the value when it uses it. Audit (Sep 28 2026)
of our mods: all live except Knee Shot Stun `ExtraKneelSeconds` (read at startup) and Billy Clubs values used when the
clubs/holsters are built (`Mass`, `ThrowSearchDistance`, `Length`, `Radius`, `BodyColor`, holster positions,
`GloveColor`), which apply on the next level load.

## Settings (`[ModSettings]`)

`PhoneTile` (true), `OpenKey` (`M`, with Ctrl), `PanelDistance` (0.4 m; fingertips reach up to ~0.6 m, past
that use the laser, which reaches 4 m, and raise `PanelScale` if the text gets small), `PanelScale` (1), `DebugLog`.
With `DebugLog`, the phone's home-screen layout is written once to `UserData/ModSettings_phone.txt`.

0.2.0 replaced the 0.1.0 right-pocket grip gesture (too hard to find: grips landed 19-27 cm from it) with the phone
tile. The old `Pocket*` keys in the cfg are unused.

## The phone tile

A copy of the Game Options tile, relabelled "Mod Settings", in the centre slot (`App_5` of the `Apps` 3x3 grid). That
slot also holds the mission Data Breach holders; while one is switched on, the tile moves to the empty left slot
(`App_4`) and returns afterwards. Placed by the grid's structure, not by
position: the home screen's local x axis is mirrored. It is made under an
inactive holder (nothing wakes before it's cleaned), its `ANBLanguageText` is removed so the label sticks, and its
`onButtonPush` events are replaced with empty ones, so the game's own action (the pause menu) never runs. A postfix on
`ANBInterfaceButton.buttonPush` opens the board. It joins `ANBSmartphone.allButtons`, which `toggleInput` shows and
hides with the phone.

## Build

```powershell
dotnet build ModSettings/ModSettings.csproj -c Release -o feature/ModSettings-1.0.0
dotnet run --project ModSettings/Tests/Tests.csproj -- "<game>/UserData/MelonPreferences.cfg"
```

The test prints how every string setting in the cfg would be shown (choice / colour / read-only), checks number
stepping and ray intersections, and verifies the direct holster adjustment hold/release gate.

## How it works

Quads (URP Unlit) + TextMeshPro 3D text under one `DontDestroyOnLoad` root, no colliders. Each frame the index
fingertip bones (`LndexNub` / `RndexNub` in the glove rig; palm as a fallback) are transformed into panel space and
tested against the button rectangles. Hands come from a Harmony postfix on `HVRHandGrabber.Start`.

Distance selection uses the native `HVRUIPointer.Camera` forward direction, with a line starting at the fingertip.
The pointing pose comes from `ANBVrHandProximityPoser` where `isVRHandPointer` is true, applied with
`HVRHandGrabber.SetAnimatorOverridePose`. The board does not call `showVRpointer`, which also changes global hand
models and locomotion flags. Pose ownership is released on aim exit, grabbing an item, close and scene change.

Build and automated logic checks are available; the headset checks in [TESTING.md](TESTING.md) still need a VR pass.

Defaults are declared by each mod in CreateEntry, not learned from first launch or your saved configuration. Mod Settings compares the current saved value with that declared default, and Reset calls ResetToDefault. Holster color's Default option preserves native game colors; saved hex colors remain editable.

**Reset section** in the footer restores all editable settings in the current category, including other scroll pages. Per-row **Reset** still restores just one setting. Hidden entries and mod-managed saved state are excluded.
