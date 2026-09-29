# VR Holster Customization 0.1.1

This MelonLoader mod works on its own with the game's hip, knife, and back holsters. It also owns the back slots for mod items, their grab and draw behavior, and their saved state. Weapon Framework uses its docking API for wall items and its back slot API for the test crowbar. Daredevil uses the back slot API for clubs.

Put `VRHolsterCustomization.dll` in the game's `Mods` folder. Install it alongside `WeaponFramework.dll` when using Weapon Framework 0.3.0 or later. Mod Settings is optional; it shows these MelonPreferences on the in-VR board.

## Position settings

All positions are in centimetres and update while the game is running. Zero leaves the game's original position. Negative values move in the opposite direction.

- `[VRHolsters_Positions] AllUpCm`: move all six game holsters up or down together.
- `LeftHip`, `RightHip`, `LeftKnife`, `RightKnife`, `LeftBack`, `RightBack` each have `UpCm` and `AlongCm`. `AlongCm` moves right for positive values and left for negative values along the waist belt (or the shoulder frame for back holsters).
- The game's actual sockets move with their highlight visuals, so the grab locations follow the settings.

The mod records each socket's original local position when it first appears and reapplies the offset late in each frame. Settings survive scene changes through MelonPreferences. Socket discovery stops once the six sockets and four highlight components are found, or after 15 seconds in a scene.

## Color

`[VRHolsters_Visual] Color` is now a `#RRGGBB` value. Mod Settings shows the same color palette used by Gloves, including a swatch; direct hex edits in `MelonPreferences.cfg` also work. The default `#FF2620` matches the previous red preset. This also tints Daredevil's club holster tubes when Daredevil is installed. The game's fade still controls whether a hologram is visible and how bright it is. Old numeric values must be replaced with a hex color.

## Game loadout saving

The game normally writes its holster loadout only when using the gun wall or leaving the Range by elevator. Version 0.1.1 saves the game's current loadout one second after a gun or knife is placed in a holster, so a pistol placed on the left persists when returning through the main menu. Pending saves are canceled during scene loads, and restore or removal events do not trigger a save.

## Mod item back slots

`[VRHolsters_BackSlots]` controls the pose, snap distance, and draw reach for non-game items registered through `Holsters.RegisterKind`. `SavedBackHolsters` is managed by the mod. Mods can call `Holsters.TryHolster(item)` when releasing an item, or `Holsters.Watch(item)` to have the mod watch for its release. `Holsters.Holds(item)` checks whether it is on the back.

These settings moved from `[WeaponFrameworkHolsters]`. Existing tuned values can be copied into the new category in `UserData/MelonPreferences.cfg` while the game is closed. The old category is no longer used.

## Build

Build `VRHolsterCustomization.csproj` with .NET 6 and the game's generated MelonLoader interop assemblies, as described in the repository README. The project references neither Weapon Framework nor Daredevil. For the combined setup, build the holster mod, Weapon Framework, and Daredevil, then put all three DLLs in `Mods`.

Compile verification is complete; the revised save, position, and color behavior still need an in-game VR pass.
