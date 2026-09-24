# Better Bow — a Gunman Contracts VR mod

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for **Gunman Contracts – Stand Alone**
(Unity 6, IL2CPP, HurricaneVR) that fixes and expands the bow:

- **Quiver.** Holster the bow on a shoulder, take it in one hand, reach back to that shoulder with
  the other hand and press grip to draw an arrow. Bring it to the string to nock it.
- **Dagger grip.** With a drawn arrow in hand, **A** (right) / **X** (left) flips it into a knife
  grip along your knuckles (tip down by default) and back. A held arrow stabs.
- **String grab on the first press.** Fixes having to press grip twice to get the next arrow when
  shooting fast.
- **Explosive barrels** detonate from one arrow instead of three.
- **Doors** with a "shoot here to burst open door" mark open to an arrow, not just a gunshot.

Every feature can be switched off in `UserData\MelonPreferences.cfg`, section `[BetterBow]`.
Arrow counts, ammo and scoring go through the game's own bow code.

## Install

1. Install [MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases) (tested with 0.7.3)
   on `GunmanContracts.exe`. **0.6.x does not work** on this Unity 6 build.
2. Put `BetterBow.dll` in the game's `Mods` folder (a prebuilt copy is in [`release/`](release/)).
3. If you used Arrow Grab Assist or Arrow Quiver before, delete those DLLs: Better Bow contains both.

## How it works

Everything was found by reading the game's IL2CPP binary; the notes are in the source comments.
In short:

| Feature | Cause in the game | What the mod does |
|---|---|---|
| Grip twice for an arrow | `HVRHandGrabber.CheckGrab` grabs only on the grip-press frame, and `CanHover` refuses new hover targets while grip is held | Buffers the press and completes it with `TryGrab` on the string |
| No quiver | The only arrow source is `HVRArrowLoader.OnStringGrabbed` | `CreateArrow(false)` spawns an un-nocked arrow into the hand; nocking hands over to the game's own string grab |
| Barrels need 3 arrows | `ANBBreakable.hit` scales bullet/explosion damage but not arrows (100 vs 250 health) | Raises arrow damage on explosive breakables to the barrel's health |
| Arrows don't breach doors | `ANBGameLogic.TryKickDoor` is only called from gun code | Calls it from `HVRPhysicsBow.ShootArrow` with the same range and mask |

## Build

Requires the .NET SDK and a game install on which MelonLoader has run once (it generates the
interop assemblies the project references).

```
cd BetterBow
dotnet build -c Release -p:GameDir="D:\path\to\Gunman Contracts - Stand Alone"
```

Or put your path in `BetterBow/GameDir.local.props` (git-ignored):

```xml
<Project><PropertyGroup><GameDir>D:\path\to\Gunman Contracts - Stand Alone</GameDir></PropertyGroup></Project>
```

## Repository layout

- `BetterBow/`: the mod. `BetterBow.cs` (entry, settings, registry), `StringGrab.cs`, `ArrowPower.cs`
  (barrels, doors), `Quiver.cs` (quiver, dagger grip).
- `il2cpp_tools/`: small Python tools for reading the game without Cpp2IL/Il2CppDumper:
  `il2.py` (global-metadata v31 + GameAssembly method/field map), `disx.py` (named disassembly),
  `xref.py` (direct callers). Needs `pefile`, `capstone`, `numpy`. Set `GUNMAN_CONTRACTS_DIR` or a
  one-line `il2cpp_tools/game_dir.txt` to your game folder.
- `QUIVER_PLAN.md`: the development log of the quiver and dagger grip.
- `release/`: the prebuilt DLL and the Nexus Mods page text.

## Licence

MIT. Not affiliated with the developer of Gunman Contracts.
