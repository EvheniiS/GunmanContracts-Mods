# Gunman Contracts Mods

[MelonLoader](https://github.com/LavaGang/MelonLoader) mods for **Gunman Contracts – Stand Alone**
(Unity 6, IL2CPP, HurricaneVR). Each mod is a separate DLL and can be installed on its own.

| Mod | What it does | Status |
|---|---|---|
| [Better Bow](BetterBow/README.md) | Quiver, dagger grip, thrown-arrow aim assist, first-press string grab, one-arrow barrels, arrows breach doors | 1.1.0, released on [Nexus](https://www.nexusmods.com/gunmancontractsstandalone/mods/22) |
| [Knee Shot Stun](KneeShotStun/KneeShotStun.cs) | Enemies shot in the leg stay down on one knee for longer | 0.1.0, in testing |
| [Fire Selector](FireSelector/FireSelector.cs) | Hold A / X on the support hand to switch automatic guns between automatic, 3-round burst and single shot | 0.1.0, in testing |

Settings for each mod are in `UserData\MelonPreferences.cfg`, one section per mod.

## Install

1. Install [MelonLoader 0.7.x](https://github.com/LavaGang/MelonLoader/releases) (tested with 0.7.3)
   on `GunmanContracts.exe`. **0.6.x does not work** on this Unity 6 build.
2. Put the mod's DLL in the game's `Mods` folder. Released builds are in [`release/`](release/).

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

- `BetterBow/`, `KneeShotStun/`, `FireSelector/`: one folder per mod, source and its own project.
- `release/<Mod>/`: the prebuilt DLL of the last release and the Nexus Mods page text.
- `il2cpp_tools/`: small Python tools for reading the game without Cpp2IL/Il2CppDumper:
  `il2.py` (global-metadata v31 + GameAssembly method/field map), `disx.py` (named disassembly),
  `xref.py` (direct callers), `scanoff.py` (which methods touch a field offset), plus asset readers
  (`bowpaint.py`, `anim_dump.py`, `clip_info.py`, `clip_root.py`). Needs `pefile`, `capstone`, `numpy`
  (asset tools: `UnityPy`). Set `GUNMAN_CONTRACTS_DIR` or a one-line `il2cpp_tools/game_dir.txt` to
  your game folder.

## Versions

`main` holds the current source of every mod. Releases are marked with tags per mod
(`betterbow-v1.1.0`, ...), so a mod "in testing" can live on `main` next to released ones.

## Licence

MIT. Not affiliated with the developer of Gunman Contracts.
