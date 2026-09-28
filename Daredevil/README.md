# Daredevil 0.1.0

The Daredevil package, one DLL: **Billy Clubs 0.11.0** (clubs from the crowbar, belt holsters, F8 recall, spin
styles, ricochets, knockouts, dark red gloves) + **Radar Sense 0.3.1** (enemy silhouettes through walls in slow
motion or always, louder 3D enemy footsteps, dropped clubs glow). **Requires Throw Assist** (`ThrowAssist/`) for
thrown pistols; the package works without it, but pistols then get the game's weak throw.

Install: `Mods/Daredevil.dll` (release zip `release/Daredevil/Daredevil-0.1.0.zip`) + `Mods/ThrowAssist.dll`.
**Remove standalone `BillyClubs.dll` and `RadarSense.dll`** if present, or everything loads twice.
Settings stay in `[BillyClubs]` and `[RadarSense]`, so a switch from the standalone mods keeps them. Log lines:
`[Daredevil]` = Billy Clubs, `[Radar Sense]` = Radar Sense.

## How it's built

No code of its own beyond `Daredevil.cs`: the project compiles `../BillyClubs/*.cs` and `../RadarSense/*.cs` with
`DAREDEVIL` defined. Under it both drop their own `MelonInfo`, `BillyClubsMod` is the one MelonMod (named
"Daredevil"), and its partial-method hooks (`PackageInit/Scene/Update`, empty in the standalone Billy Clubs build) run
Radar Sense's static `Init/Scene/Tick`. The standalone mods still build and work on their own. The club model and
textures are embedded under the same resource names as in Billy Clubs.

```powershell
dotnet build Daredevil/Daredevil.csproj -c Release -o feature/Daredevil-0.1.0
```

**Version bumps:** a change in either mod means a new Daredevil version too; list both inner versions here.
