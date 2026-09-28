# Gunman Contracts Mods

[MelonLoader](https://github.com/LavaGang/MelonLoader) mods for **Gunman Contracts – Stand Alone**
(Unity 6, IL2CPP, HurricaneVR). Each mod is a separate DLL. Most stand alone; the **Daredevil** package needs a few
of the others (see [Mod structure](#mod-structure)).

| Mod | What it does | Status |
|---|---|---|
| [Better Bow](BetterBow/README.md) | Quiver, dagger grip, thrown-arrow aim assist, first-press string grab, full draw power without a perfect pull, one-arrow barrels, arrows breach doors | 1.2.0, released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/22) |
| [Knee Shot Stun](KneeShotStun/KneeShotStun.cs) | Enemies shot in the leg stay down on one knee for longer | 1.1.0 (`AllowBurst` toggle), release candidate; 1.0.0 released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/26) |
| [Heavy Melee](HeavyMelee/HeavyMelee.cs) | Guns and bows hit like heavy metal (stagger + damage), fists hit harder, and an enemy on the ground still takes hits, stays down, and can be knocked out | 1.0.0 release candidate (not released) |
| [Physical Dodge](PhysicalDodge/PhysicalDodge.cs) | Enemies aim where you were a moment ago: stand still and you get hit; step, lean, duck or keep moving and their shots go past (a short buzz and a brief slow motion when one would have hit) | 0.4.2, in testing |
| [Fire Selector](FireSelector/FireSelector.cs) | Hold A / X on the support hand to switch automatic guns between automatic, 3-round burst (optional) and single shot | 1.1.0 (`AllowBurst` toggle), release candidate; 1.0.0 released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/25) |
| [Enemy Awareness Fix](EnemyAwarenessFix/EnemyAwarenessFix.cs) | Enemies that lose sight of you go to where they last saw you and search there instead of walking to your exact position; wave enemies get a rough guess; flanking and turning use what the enemy knows; wave spawns no longer always use the nearest spawn point and keep a 10 m minimum distance; floor-aware (no hearing you through a floor, no "arrived" on the wrong floor) | 0.1.6, in testing ("getting better") |
| [Throw Assist](ThrowAssist/README.md) | Real-physics steering for the game's own throw assist, and thrown pistols | 0.1.0, release candidate, untested |
| [Mod Settings](ModSettings/README.md) | In-VR settings board listing every mod's preferences, live-applied | 0.2.2, board tested and works |
| [Enemy Awareness Log](EnemyAwarenessLog/EnemyAwarenessLog.cs) | Diagnostic, changes nothing: logs spawns, where each enemy first sees you, whether unseen enemies track your live position, your shots and which sounds alert whom, door kicks (and door loops), stuck enemies, which floor each enemy is on, who is still alive at the end, your stealth visibility, harmless enemy shots, and per-wave summaries | 0.5.2, dev tool |
| [Grab Log](GrabLog/README.md) | Diagnostic: grip timing, prompt and hover changes, hand alignment, item distances, detection volumes, actual grab results, and pickup state after release/throw | 0.1.0, compiled; VR testing pending |
| [Daredevil](Daredevil/README.md) | **The Daredevil package, the only build of these features:** textured billy clubs from The Range crowbar (blunt damage, homing throw, ricochets, knockouts, belt holsters that carry into contracts, F8 recall, arsenal panel entry with Weapon Framework) and radar sense (red enemy silhouettes through walls, louder steps, glowing dropped clubs). Requires Gloves, Throw Assist and Mod Settings. The standalone Billy Clubs and Radar Sense mods are legacy and removed from this repo | 0.3.3, tested well; not yet released on Nexus |
| [Gloves](Gloves/README.md) | Recolours the player's gloves: dark red by default, any colour from the Mod Settings board, applied at once | 0.1.0, required by Daredevil |
| [Weapon Framework](WeaponFramework/README.md) | Lets mods add their own weapons to the arsenal panel in The Range (page to it, press Retrieve, take it off the wall); used by Daredevil. Guide for adding your own weapon: [MODDING_GUIDE.md](WeaponFramework/MODDING_GUIDE.md) | 0.1.1, tested; release candidate in `release/WeaponFramework/` |

Settings for each mod are in `UserData\MelonPreferences.cfg`, one section per mod, and on the in-VR Mod Settings board.

## Mod structure

**Daredevil** is one package and the only place its features are built:

```
Daredevil.dll            clubs (Daredevil/BillyClubs/) + radar sense (Daredevil/RadarSense/)
 ├─ requires  Gloves.dll           red gloves by default, any colour from the board
 ├─ requires  ThrowAssist.dll      thrown pistols that home and hurt
 ├─ requires  ModSettings.dll      the in-VR settings board
 └─ optional  WeaponFramework.dll  Billy Clubs on the arsenal panel in The Range
```

- Daredevil still loads without a required mod and logs which ones are missing.
- **Legacy (Sep 28 2026):** the standalone Billy Clubs and Radar Sense mods. Their code now lives only inside
  Daredevil; the last standalone versions (0.12.0 / 0.3.1) are in git history. Remove `BillyClubs.dll` and
  `RadarSense.dll` from `Mods` if you have them, or everything loads twice. Their settings sections (`[BillyClubs]`,
  `[RadarSense]`) are still the ones Daredevil uses.
- **Gloves** replaces the old `[BillyClubs] GloveColor` setting with `[Gloves] Color`.
- **Weapon Framework** is for any mod: register a weapon and it appears on the arsenal panel (API in its README).

Parked ideas (hardcore "no free misses") with the game code behind them: [IDEAS.md](IDEAS.md).

## Install

1. Install [MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases) (tested with 0.7.3)
   on `GunmanContracts.exe`. **0.6.x does not work** on this Unity 6 build.
2. Put the mod's DLL in the game's `Mods` folder. Released builds are in [`release/`](release/).
   For Daredevil, also put in `Gloves.dll`, `ThrowAssist.dll` and `ModSettings.dll` (and optionally `WeaponFramework.dll`).

## Build

Requires the .NET SDK and a game install on which MelonLoader has run once (it generates the
interop assemblies the projects reference). Each mod folder is its own project:

```
cd BetterBow
dotnet build -c Release -p:GameDir="D:\path\to\Gunman Contracts - Stand Alone"
```

Or put your path in `<ModFolder>/GameDir.local.props` (git-ignored):

```xml
<Project><PropertyGroup><GameDir>D:\path\to\Gunman Contracts - Stand Alone</GameDir></PropertyGroup></Project>
```

## Repository layout

- `BetterBow/`, `KneeShotStun/`, `FireSelector/`, `HeavyMelee/`, `PhysicalDodge/`, `EnemyAwarenessFix/`,
  `EnemyAwarenessLog/`, `ThrowAssist/`, `ModSettings/`, `Gloves/`, `WeaponFramework/`: one folder per mod, source and
  its own project.
- `Daredevil/`: the package project, with `BillyClubs/` and `RadarSense/` source folders and `Tests/` (the model
  loader test). There are no separate `BillyClubs/` or `RadarSense/` projects any more.
- `BlenderRefs/`: the billy club model (`out/billy_club.obj` + textures, embedded in Daredevil) and
  `render_arsenal_icon.py` / `make_arsenal_icon.py`, which render its arsenal panel picture.
- `release/<Mod>/`: the prebuilt DLL of the last release and the Nexus Mods page text.
- `release/NEXUS_TEMPLATE.txt`: the shared skeleton behind every `NEXUS_DESCRIPTION.txt` (Install,
  Settings, Compatibility, Uninstall, Source boilerplate) — start a new mod's page from it.
- `il2cpp_tools/`: small Python tools for reading the game without Cpp2IL/Il2CppDumper:
  `il2.py` (global-metadata v31 + GameAssembly method/field map), `disx.py` (named disassembly),
  `xref.py` (direct callers), `scanoff.py` (which methods touch a field offset), `slot.py` (string literals and generic types a method uses), `dumpt.py` (fields/methods of a type), `disa.py` (whole-method disassembly
  with calls and metadata annotated), plus asset readers (`bowpaint.py`, `anim_dump.py`, `clip_info.py`,
  `clip_root.py`, `prefab_tree.py` (a prefab/scene hierarchy with component classes), `gunwall_scan.py` (the arsenal
  walls)). Needs `pefile`, `capstone`, `numpy`
  (asset tools: `UnityPy`). Set `GUNMAN_CONTRACTS_DIR` or a one-line `il2cpp_tools/game_dir.txt` to
  your game folder.

## Versions

Two branches: `main` = what is released on Nexus, `dev` = unreleased work. Releases are marked with tags per mod
(`betterbow-v1.1.0`, ...).

## Licence

MIT. Not affiliated with the developer of Gunman Contracts.
