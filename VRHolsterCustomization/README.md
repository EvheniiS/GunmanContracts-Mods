# VR Holster Customization 0.3.6 (built, untested; 0.2.2 is the release candidate)

This MelonLoader mod customizes the game's hip, knife, and back holsters and requires Mod Settings. It also owns the back slots for mod items, their grab and draw behavior, and their saved state. Weapon Framework uses its docking API for wall items and its back slot API for the test crowbar. Daredevil uses the back slot API for clubs.

Put `VRHolsterCustomization.dll` and **Mod Settings 0.4.1 or newer** (`ModSettings.dll`) in the game's `Mods` folder.
[Mod Settings](https://www.nexusmods.com/gunmancontractsstandalone/mods/28) is a **hard requirement**, declared in the mod's MelonLoader dependency metadata. The standalone ZIP requires installing it separately; the With-ModSettings ZIP includes 1.0.0. Install only one package. Weapon Framework and Daredevil are optional integrations.

## Position settings

All positions are in centimetres and update while the game is running. Zero leaves the game's original position. Negative values move in the opposite direction.

- `[VRHolsters_Positions] AllUpCm`: move all six game holsters up or down together.
- `LeftHip`, `RightHip`, `LeftKnife`, `RightKnife`, `LeftBack`, `RightBack` each have `UpCm`, `AlongCm`, and `ForwardCm`.
  Positive values move up, right, and forward along the waist belt (or shoulder frame for back holsters).
  Mod Settings displays readable names with units; existing preference keys and offsets are preserved.
- The game's actual sockets move with their highlight visuals, so the grab locations follow the settings.

The mod records each socket's original local position when it first appears and reapplies the offset late in each frame. Settings survive scene changes through MelonPreferences. Socket discovery stops once the six sockets and four highlight components are found, or after 15 seconds in a scene.

## Move an empty holster by hand (experimental)

1. Enable **Move empty holsters by hand** under **VR Holsters: move by hand** (`[VRHolsters_Adjustment] Enabled`).
2. Empty your hand and the game holster. Bring your palm within 12 cm; a blue marker identifies the nearest empty slot.
3. Hold **grip + trigger** there for **two seconds**. Amber and small pulses mean keep holding; green and a stronger
   pulse mean you can move. Pressing the buttons before approaching does not start a hold.
4. Keep both held and move your hand. The holster follows on all three body-frame axes in 1 cm steps, normally within
   ±50 cm of its original position. Release either button to save. Turn adjustment off when finished.

Leaving the slot during the initial hold cancels it; picking up an item, opening pause, disabling adjustment or changing
scenes ends the gesture and saves any movement. A canceled hold requires releasing the buttons before retrying.
Body movement and snap turns are measured relative to the holster frame. `AllUpCm` stays separate from per-slot offsets.
This first implementation moves the six game sockets only; Weapon Framework / Daredevil mod-item back slots retain
their separate settings. Use each position row's Reset in Mod Settings to undo a calibration.

## Color

`[VRHolsters_Visual] Color` accepts `Default` or a `#RRGGBB` value. Mod Settings shows the same color palette used by Gloves, including a swatch; direct hex edits in `MelonPreferences.cfg` also work. The default `Default` preserves the original game colors captured from each socket, without guessing a yellow shade. A saved custom hex color is a changed value; Reset restores the game colors. This also tints Daredevil's club holster tubes when Daredevil is installed. The game's fade still controls whether a hologram is visible and how bright it is. Old numeric values must be replaced with a hex color.

## Game loadout saving

The game normally writes its holster loadout only when using the gun wall or leaving the Range by elevator. Version 0.2.1 saves the loadout one second after placing a gun or knife in a holster **in The Range only**. Contract holster activity no longer overwrites the prepared loadout: putting a knife away while the left pistol is drawn used to save that pistol slot as empty. Pending saves are canceled during scene loads, and restore or removal events do not trigger a save.

Version 0.2.2 also saves mod-item back slots only in The Range. Drawing or losing an item
during a contract leaves the prepared back-slot loadout available for a death/retry. On a same-scene
checkpoint reset it reuses a tracked or previously drawn item (including Weapon Framework's crowbar)
and spawns one only if the original is gone. The user confirmed this fix in game on September 30, 2026.

## Mod item back slots

`[VRHolsters_BackSlots]` controls the pose, snap distance, and draw reach for non-game items registered through `Holsters.RegisterKind`. `SavedBackHolsters` is managed by the mod. Mods can call `Holsters.TryHolster(item)` when releasing an item, or `Holsters.Watch(item)` to have the mod watch for its release. `Holsters.Holds(item)` checks whether it is on the back.

These settings moved from `[WeaponFrameworkHolsters]`. Existing tuned values can be copied into the new category in `UserData/MelonPreferences.cfg` while the game is closed. The old category is no longer used.

## Katana and knives on the back (0.3.0, untested)

The game's katanas and knives fit the two back slots too, one item per side, like a mod item.

- `[VRHolsters_BackSlots] BackBlades`: `All` (katanas and knives, default), `Katana` (katanas only) or `Off`.
  Arrows and pens are never accepted.
- Take a blade in your hand, bring it over a shoulder (the hand buzzes over a free slot) and let go slowly. A fast
  release is a throw and stays a throw. Draw it with the same grip press as a crowbar (`DrawCm`).
- It hangs by its grip end, the flat of the blade against your back: `BladeGripCm` (grip below the holster point),
  `BladeTiltDeg` (35 = diagonal), `BladeLeanDeg`, `BladeSpinDeg`. `OutCm`, `UpCm` and `BackCm` are shared with mod items.
- **0.3.1:** anything in a back slot (blade, crowbar, club) is non-solid while it is holstered, like the game's own
  holstered guns: its solid colliders become triggers, so a bow or gun drawn from the other shoulder passes through it.
  They turn solid again when you draw it.
- **0.3.2:** a blade you drew from your back returns to your back after a throw or a drop (its own side if free,
  else the other free side), not to the belt knife holster. Taken from the belt holster or the wall, or put in the belt
  holster, the game's own return applies again. If both back sides are full, the game's return applies.
- **0.3.5 fix:** a blade auto-returned to the back could arrive already non-solid (the game makes a knife's colliders
  triggers while it flies or sticks in an enemy). Only solid colliders were recorded, so nothing turned solid on the
  draw, and every katana thrown after a return flew through enemies, walls and the floor (Oct 3 2026 logs: no body
  collision, then ~40 m/s = falling out of the map). The knife's own list of normally solid colliders
  (`ANBKnife.nonTriggerColliders`) is now restored on the draw too; the log line says `(N already were, solid again on the draw)`.
- The game sends a dropped knife back to its holster or wall spot after a few seconds. That timer is held at zero while
  the blade is on your back.
- Saved like a mod item (`Knife-<id>` in `SavedBackHolsters`, The Range only). After a scene load or a checkpoint
  reset the blade comes from where the game gets its holster knives: in The Range the knife wall (if that knife is
  already in a knife holster, the back side stays empty), in a contract a new copy of the knife (0.3.4; contracts have
  no knife wall). In the main menu it waits for the next scene.

## Knife return time (0.3.6, untested)

A thrown or dropped knife or katana returns to its holster (or back slot) on its own. `[VRHolsters_KnifeReturn]` sets
how long it waits, in seconds, both live from Mod Settings. 0 = the game's own timing (with `DebugLog` the game's delay
is logged at the first release).

- `InEnemySeconds`: how long it stays in an enemy, counted from when it sticks in or kills one. 0.5 = back half a
  second after the hit; 5 = it stays a while.
- `OnGroundSeconds`: how long it lies where it landed (floor, wall), counted from when it comes to rest. While it is
  still flying it never returns, however short this is.

The game's own knife auto-return must be on; with it off, nothing changes.

## Build

Build `VRHolsterCustomization.csproj` with .NET 6 and the game's generated MelonLoader interop assemblies, as described in the repository README. The project references neither Weapon Framework nor Daredevil. For the combined setup, build the holster mod, Weapon Framework, and Daredevil, then put all three DLLs in `Mods`.

Compile and hold-state verification are available; direct adjustment, native socket behaviour and the revised menu
still need the in-game VR pass in [ModSettings/TESTING.md](../ModSettings/TESTING.md).
