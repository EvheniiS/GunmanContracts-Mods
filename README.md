# Gunman Contracts Mods

[MelonLoader](https://github.com/LavaGang/MelonLoader) mods for **Gunman Contracts – Stand Alone**
(Unity 6, IL2CPP, HurricaneVR). Each mod is a separate DLL. Most stand alone; the **Daredevil** package needs a few
of the others (see [Mod structure](#mod-structure)).

| Mod | What it does | Status |
|---|---|---|
| [Better Bow](BetterBow/README.md) | Quiver, dagger grip, thrown-arrow aim assist, first-press string grab, full draw power without a perfect pull, one-arrow barrels, arrows breach doors | 1.2.0, released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/22) |
| [Knee Shot Stun](KneeShotStun/KneeShotStun.cs) | Enemies shot in the leg stay down on one knee for longer | 1.1.0 (`AllowBurst` toggle), release candidate; 1.0.0 released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/26) |
| [Heavy Melee](HeavyMelee/HeavyMelee.cs) | Guns and bows hit like heavy metal (stagger + damage), fists hit harder, and an enemy on the ground still takes hits, stays down, and can be knocked out | 1.0.0, release candidate; not a priority right now |
| [Physical Dodge](PhysicalDodge/PhysicalDodge.cs) | Enemies aim where you were a moment ago: stand still and you get hit; step, lean, duck or keep moving and their shots go past (a short buzz and a brief slow motion when one would have hit) | 0.4.2, release candidate; only preference fine-tuning left (personal taste) |
| [Fire Selector](FireSelector/FireSelector.cs) | Hold A / X on the support hand to switch automatic guns between automatic, 3-round burst (optional) and single shot | 1.1.0 (`AllowBurst` toggle), release candidate; 1.0.0 released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/25) |
| [Stealth AI Fix](EnemyAwarenessFix/EnemyAwarenessFix.cs) | Enemies that lose sight of you go to where they last saw you and search there instead of walking to your exact position; wave enemies get a rough guess; flanking and turning use what the enemy knows; wave spawns no longer always use the nearest spawn point and keep a 10 m minimum distance; floor-aware (no hearing you through a floor, no "arrived" on the wrong floor) | 0.1.6, ready for 1.0; [Enemy Awareness Log](EnemyAwarenessLog/EnemyAwarenessLog.cs) ships alongside it as an optional file |
| [Throw Assist](ThrowAssist/README.md) | Real-physics steering for the game's own throw assist, and thrown pistols | 0.2.2 packaged release candidate; 3.5 m/s default assist gate |
| [Mod Settings](ModSettings/README.md) | Movable VR settings board with native pointing pose, laser input and per-setting reset; mouse menu in flat mode | 1.0.0 packaged (0.2.2 released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/28)) |
| [Grab Log](GrabLog/README.md) | Diagnostic: grip timing, prompt and hover changes, hand alignment, item distances, detection volumes, actual grab results, and pickup state after release/throw | 0.1.0, in testing |
| [Frame Probe](FrameProbe/README.md) | Diagnostic frame timing and draw-call summaries for controlled mod-performance comparisons; optional Jev log filter | 0.1.0 diagnostic; in-game verification pending |
| [Challenge NPC Limit](ChallengeNpcLimit/README.md) | Raises the selectable enemy count in takedown challenges above the game's 30 limit (60 by default) | 0.1.0, built and installed for local testing; in-game verification pending |
| [Grab Fix](GrabFix/GrabFix.cs) | Finger + palm-aimed pickup together (wider near sphere, grip buffer, thrown-item catch), so more grabs land without accidental grabs (grip is still a deliberate button) | 0.2.0, in testing |
| [Daredevil](Daredevil/README.md) | Billy clubs, belt holsters, arsenal panel entry with Weapon Framework, radar sense, and club door kicks. Club back slots use VR Holster Customization. | 1.0.0 packaged release candidate; checkpoint and door kick tested in game |
| [Slow Motion Hands](SlowMotionHands/README.md) | Hands keep up with your controllers in slow motion: normal-rate physics plus hand drives scaled by the time scale | 0.1.3 tested: slow motion feels much better (rifle/club not yet logged) |
| [Aim Colors](AimColors/README.md) | Recolour the weapon laser (beam and dot, with a brightness boost), the iron sights and the collimator reticle (colour, brightness, size), live from Mod Settings | 0.1.0 release candidate (tested in game Oct 2 2026) |
| [Melee Unlocks](MeleeUnlocks/README.md) | Unlocks knives on The Range knife wall as if you had made the kills; by default the double katana (second katana for dual wielding), which no contract places. `All` unlocks every knife. Saved in the game save | 0.2.1, tested Oct 3 2026 (double katana unlocked and saved; `All` untested) |
| [Gloves](Gloves/README.md) | Recolours the player's gloves: dark red by default, any colour from the Mod Settings board, applied at once | 0.1.0, released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/32); required by Daredevil |
| [Weapon Framework](WeaponFramework/README.md) | Adds mod weapons to the arsenal panel in The Range; uses VR Holster Customization for item docking and test crowbar back slots. Guide: [MODDING_GUIDE.md](WeaponFramework/MODDING_GUIDE.md) | 0.3.0 on dev; requires VR Holster Customization |
| [VR Holster Customization](VRHolsterCustomization/README.md) | Adjust game holsters on three axes or move empty slots by hand; hologram colors and mod-item back slots | 0.2.2 packaged release candidate; back-slot checkpoint reset tested; requires Mod Settings |
| [Death Details](DeathDetails/README.md) | Dead enemies close their eyes, mouth slightly parted (the faces' own ARKit shapes; corpse faces cost less than vanilla); enemies writhing in pain keep their pain face until they die, and a melee hit to the head finishes them with a head-hit sound. Optional, experimental: bleed-out, any hit finishes them | 0.7.1 release candidate (tested); experimental options off, untested |

Settings for each mod are in `UserData\MelonPreferences.cfg`, one section per mod, and on the in-VR Mod Settings board.

For game updates, use the [recovery workflow](Tools/UPDATE_RECOVERY.md):
`./Tools/Update-Triage.ps1 Prepare`, play the smoke checklist, then `Collect -Session <printed folder>` and
`Restore -Session <printed folder>`. It backs up preferences, enables installed mods' diagnostics, preserves session
evidence and helps choose selective repairs. Use `Snapshot` now to retain a pre-update baseline.

## Mod structure

**Daredevil** is one package and the only place its features are built:

```
Daredevil.dll            clubs (Daredevil/BillyClubs/) + radar sense (Daredevil/RadarSense/)
 ├─ requires  Gloves.dll           red gloves by default, any colour from the board
 ├─ requires  ThrowAssist.dll      thrown pistols that home and hurt
 ├─ requires  ModSettings.dll      the in-VR settings board
 ├─ optional  VRHolsterCustomization.dll  club back slots and holster color
 └─ optional  WeaponFramework.dll  Billy Clubs on the arsenal panel in The Range
```

- Daredevil still loads without a required mod and logs which ones are missing.
- **Legacy (Sep 28 2026):** the standalone Billy Clubs and Radar Sense mods. Their code now lives only inside
  Daredevil; the last standalone versions (0.12.0 / 0.3.1) are in git history. Remove `BillyClubs.dll` and
  `RadarSense.dll` from `Mods` if you have them, or everything loads twice. Their settings sections (`[BillyClubs]`,
  `[RadarSense]`) are still the ones Daredevil uses.
- **Gloves** replaces the old `[BillyClubs] GloveColor` setting with `[Gloves] Color`.
- **Weapon Framework** is for any mod: register a weapon and it appears on the arsenal panel (API in its README). Version 0.3.0 requires VR Holster Customization.

Parked ideas (hardcore "no free misses") with the game code behind them: [IDEAS.md](IDEAS.md).
How the game plays sounds and how to add custom ones: [SOUND.md](SOUND.md).

## Install

1. Install [MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases) (tested with 0.7.3)
   on `GunmanContracts.exe`. **0.6.x does not work** on this Unity 6 build.
2. Put the mod's DLL in the game's `Mods` folder. Released builds are in [`release/`](release/).
   For Daredevil, also put in `Gloves.dll`, `ThrowAssist.dll` and `ModSettings.dll`. Add `VRHolsterCustomization.dll` for club back slots; Weapon Framework 0.3.0 also requires it.

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

- `BetterBow/`, `KneeShotStun/`, `FireSelector/`, `HeavyMelee/`, `PhysicalDodge/`, `EnemyAwarenessFix/`, `ChallengeNpcLimit/`,
  `EnemyAwarenessLog/`, `ThrowAssist/`, `ModSettings/`, `Gloves/`, `WeaponFramework/`, `VRHolsterCustomization/`, `GrabLog/`, `MeleeUnlocks/`, `GrabFix/`, `FrameProbe/`, `DeathDetails/`: one
  folder per mod, source and its own project.
- `Daredevil/`: the package project, with `BillyClubs/` and `RadarSense/` source folders and `Tests/` (the model
  loader test). There are no separate `BillyClubs/` or `RadarSense/` projects any more.
- `BlenderRefs/`: the billy club model (`out/billy_club.obj` + textures, embedded in Daredevil) and
  `render_arsenal_icon.py` / `make_arsenal_icon.py`, which render its arsenal panel picture.
- `release/<Mod>/`: the prebuilt DLL of the last release and the Nexus Mods page text.
- `.claude/skills/nexus-release/` and `.claude/skills/nexus-status/`: the Nexus workflow (page-text template, packaging, what is live).
  `Tools/Pack-Release.ps1` builds + zips + lints a release; `Tools/Check-Nexus.ps1` asks the Nexus API what is live.
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
